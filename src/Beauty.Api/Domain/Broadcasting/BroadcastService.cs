using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Models.Broadcasting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beauty.Api.Domain.Broadcasting;

public interface IBroadcastService
{
    Task<BroadcastCampaign> CreateCampaignAsync(
        string title,
        string subject,
        string body,
        BroadcastChannelType channel,
        long? segmentId,
        string description,
        ClaimsPrincipal user);

    Task<BroadcastCampaign> ScheduleCampaignAsync(long campaignId, DateTime scheduledFor, ClaimsPrincipal user);

    Task<BroadcastCampaign> SendCampaignNowAsync(long campaignId, ClaimsPrincipal user);

    Task CancelCampaignAsync(long campaignId, ClaimsPrincipal user);

    Task<BroadcastSegment> CreateSegmentAsync(
        string name,
        BroadcastTargetRole? targetRole,
        string? locationIds,
        string? artistIds,
        int? bookedWithinDays,
        bool includeInactive);

    Task<List<string>> GetSegmentRecipientsAsync(long segmentId);

    Task<BroadcastCampaign?> GetCampaignAsync(long campaignId);

    Task<List<BroadcastCampaign>> GetAllCampaignsAsync(int page = 1, int pageSize = 20);

    Task<List<BroadcastRecipient>> GetCampaignRecipientsAsync(long campaignId);
}

public sealed class BroadcastService : IBroadcastService
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ILogger<BroadcastService> _logger;

    public BroadcastService(
        BeautyDbContext db,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        ITemplateRenderer templateRenderer,
        ILogger<BroadcastService> logger)
    {
        _db = db;
        _userManager = userManager;
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _logger = logger;
    }

    public async Task<BroadcastCampaign> CreateCampaignAsync(
        string title,
        string subject,
        string body,
        BroadcastChannelType channel,
        long? segmentId,
        string description,
        ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("User not authenticated");

        var campaign = new BroadcastCampaign
        {
            Title = title,
            Subject = subject,
            Body = body,
            Channel = channel,
            Status = BroadcastStatus.Draft,
            Description = description,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            SegmentId = segmentId
        };

        _db.BroadcastCampaigns.Add(campaign);

        // Log audit
        _db.BroadcastAuditLogs.Add(new BroadcastAuditLog
        {
            CampaignId = campaign.CampaignId,
            AdminUserId = userId,
            Action = BroadcastAuditAction.Created,
            Details = $"Campaign created: {title}",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation($"[BROADCAST] Campaign created: {campaign.CampaignId} by {userId}");

        return campaign;
    }

    public async Task<BroadcastCampaign> ScheduleCampaignAsync(
        long campaignId,
        DateTime scheduledFor,
        ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("User not authenticated");

        var campaign = await _db.BroadcastCampaigns.FindAsync(campaignId)
            ?? throw new InvalidOperationException($"Campaign {campaignId} not found");

        if (campaign.Status != BroadcastStatus.Draft)
            throw new InvalidOperationException($"Cannot schedule campaign in {campaign.Status} status");

        campaign.Status = BroadcastStatus.Scheduled;
        campaign.ScheduledFor = scheduledFor;

        // Populate recipients based on segment
        if (campaign.SegmentId.HasValue)
        {
            var recipients = await GetSegmentRecipientsAsync(campaign.SegmentId.Value);
            foreach (var email in recipients)
            {
                _db.BroadcastRecipients.Add(new BroadcastRecipient
                {
                    CampaignId = campaignId,
                    RecipientEmail = email,
                    Status = BroadcastDeliveryStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // Log audit
        _db.BroadcastAuditLogs.Add(new BroadcastAuditLog
        {
            CampaignId = campaignId,
            AdminUserId = userId,
            Action = BroadcastAuditAction.Scheduled,
            Details = $"Scheduled for {scheduledFor:u}",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation($"[BROADCAST] Campaign {campaignId} scheduled for {scheduledFor}");

        return campaign;
    }

    public async Task<BroadcastCampaign> SendCampaignNowAsync(
        long campaignId,
        ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("User not authenticated");

        var campaign = await _db.BroadcastCampaigns
            .Include(c => c.Recipients)
            .Include(c => c.Segment)
            .FirstOrDefaultAsync(c => c.CampaignId == campaignId)
            ?? throw new InvalidOperationException($"Campaign {campaignId} not found");

        campaign.Status = BroadcastStatus.Sending;
        await _db.SaveChangesAsync();

        // If no recipients yet, populate from segment
        if (!campaign.Recipients.Any() && campaign.SegmentId.HasValue)
        {
            var recipients = await GetSegmentRecipientsAsync(campaign.SegmentId.Value);
            foreach (var email in recipients)
            {
                campaign.Recipients.Add(new BroadcastRecipient
                {
                    CampaignId = campaignId,
                    RecipientEmail = email,
                    Status = BroadcastDeliveryStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();
        }

        // Send to all recipients
        var failedCount = 0;
        foreach (var recipient in campaign.Recipients.Where(r => r.Status == BroadcastDeliveryStatus.Pending))
        {
            try
            {
                // For email, render the email template
                var html = _templateRenderer.Render("broadcast_email", new Dictionary<string, string>
                {
                    ["Subject"] = campaign.Subject,
                    ["Body"] = campaign.Body,
                    ["Year"] = DateTime.UtcNow.Year.ToString()
                });

                await _emailSender.SendHtmlAsync(recipient.RecipientEmail, campaign.Subject, html);

                recipient.Status = BroadcastDeliveryStatus.Sent;
                recipient.SentAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                recipient.Status = BroadcastDeliveryStatus.Failed;
                recipient.ErrorMessage = ex.Message;
                failedCount++;
                _logger.LogError($"[BROADCAST] Failed to send campaign {campaignId} to {recipient.RecipientEmail}: {ex.Message}");
            }
        }

        campaign.Status = BroadcastStatus.Sent;
        campaign.SentAt = DateTime.UtcNow;

        _db.BroadcastAuditLogs.Add(new BroadcastAuditLog
        {
            CampaignId = campaignId,
            AdminUserId = userId,
            Action = BroadcastAuditAction.Sent,
            Details = $"Sent to {campaign.Recipients.Count - failedCount}/{campaign.Recipients.Count} recipients",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation($"[BROADCAST] Campaign {campaignId} sent to {campaign.Recipients.Count} users ({failedCount} failed)");

        return campaign;
    }

    public async Task CancelCampaignAsync(long campaignId, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("User not authenticated");

        var campaign = await _db.BroadcastCampaigns.FindAsync(campaignId)
            ?? throw new InvalidOperationException($"Campaign {campaignId} not found");

        if (campaign.Status == BroadcastStatus.Sent)
            throw new InvalidOperationException("Cannot cancel a campaign that has already been sent");

        campaign.Status = BroadcastStatus.Cancelled;

        _db.BroadcastAuditLogs.Add(new BroadcastAuditLog
        {
            CampaignId = campaignId,
            AdminUserId = userId,
            Action = BroadcastAuditAction.Cancelled,
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation($"[BROADCAST] Campaign {campaignId} cancelled by {userId}");
    }

    public async Task<BroadcastSegment> CreateSegmentAsync(
        string name,
        BroadcastTargetRole? targetRole,
        string? locationIds,
        string? artistIds,
        int? bookedWithinDays,
        bool includeInactive)
    {
        var segment = new BroadcastSegment
        {
            Name = name,
            TargetRole = targetRole,
            LocationIds = locationIds,
            ArtistIds = artistIds,
            BookedWithinDays = bookedWithinDays,
            IncludeInactive = includeInactive,
            CreatedAt = DateTime.UtcNow
        };

        // Calculate estimated recipient count
        var recipients = await GetSegmentRecipientsAsync(0); // will use resolved values
        segment.EstimatedRecipientCount = recipients.Count;

        _db.BroadcastSegments.Add(segment);
        await _db.SaveChangesAsync();

        _logger.LogInformation($"[BROADCAST] Segment created: {segment.SegmentId} ({name})");

        return segment;
    }

    public async Task<List<string>> GetSegmentRecipientsAsync(long segmentId)
    {
        var segment = await _db.BroadcastSegments.FindAsync(segmentId);
        if (segment == null)
            return new List<string>();

        var recipients = new HashSet<string>();

        // Start with all active users
        var usersQuery = _db.Users.AsQueryable();

        // Filter by role
        if (segment.TargetRole.HasValue && segment.TargetRole != BroadcastTargetRole.All)
        {
            var roleName = segment.TargetRole switch
            {
                BroadcastTargetRole.Admin => "Admin",
                BroadcastTargetRole.Artist => "Artist",
                BroadcastTargetRole.Client => "Client",
                BroadcastTargetRole.Location => "Location",
                _ => ""
            };

            var usersInRole = await _userManager.GetUsersInRoleAsync(roleName);
            var userIdsInRole = usersInRole.Select(u => u.Id).ToList();
            usersQuery = usersQuery.Where(u => userIdsInRole.Contains(u.Id));
        }

        // Filter by active status
        if (!segment.IncludeInactive)
        {
            usersQuery = usersQuery.Where(u => u.IsActive);
        }

        // Filter by location
        if (!string.IsNullOrEmpty(segment.LocationIds))
        {
            try
            {
                var locationIdList = JsonSerializer.Deserialize<List<long>>(segment.LocationIds) ?? new();
                usersQuery = usersQuery.Where(u => u.LocationId.HasValue && locationIdList.Contains(u.LocationId.Value));
            }
            catch { }
        }

        // Filter by artist
        if (!string.IsNullOrEmpty(segment.ArtistIds))
        {
            try
            {
                var artistIdList = JsonSerializer.Deserialize<List<long>>(segment.ArtistIds) ?? new();
                usersQuery = usersQuery.Where(u => u.ArtistId.HasValue && artistIdList.Contains(u.ArtistId.Value));
            }
            catch { }
        }

        var users = await usersQuery.ToListAsync();

        foreach (var user in users)
        {
            if (!string.IsNullOrEmpty(user.Email))
                recipients.Add(user.Email);
        }

        // Filter by booking activity
        if (segment.BookedWithinDays.HasValue && segment.BookedWithinDays > 0)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-segment.BookedWithinDays.Value);
            var usersWithRecentBookings = await _db.Bookings
                .Where(b => b.StartsAt >= cutoffDate)
                .Select(b => b.CustomerId)
                .Distinct()
                .ToListAsync();

            // Intersect with recent bookers
            var recentBookerEmails = await _db.Customers
                .Where(c => usersWithRecentBookings.Contains(c.Id))
                .Select(c => c.Email)
                .ToListAsync();

            recipients.IntersectWith(recentBookerEmails);
        }

        return recipients.ToList();
    }

    public async Task<BroadcastCampaign?> GetCampaignAsync(long campaignId)
    {
        return await _db.BroadcastCampaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CampaignId == campaignId);
    }

    public async Task<List<BroadcastCampaign>> GetAllCampaignsAsync(int page = 1, int pageSize = 20)
    {
        return await _db.BroadcastCampaigns
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<BroadcastRecipient>> GetCampaignRecipientsAsync(long campaignId)
    {
        return await _db.BroadcastRecipients
            .Where(r => r.CampaignId == campaignId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }
}
