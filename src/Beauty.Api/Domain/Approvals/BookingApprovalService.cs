using Beauty.Api.Contracts;
using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Domain.Approvals;

public sealed class BookingApprovalService : IBookingApprovalService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BeautyDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ITemplateRenderer _templateRenderer;

    public BookingApprovalService(
        UserManager<ApplicationUser> userManager,
        BeautyDbContext db,
        IEmailSender emailSender,
        ITemplateRenderer templateRenderer)
    {
        _userManager = userManager;
        _db = db;
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
    }

    public IReadOnlyList<PendingUserDto> GetPendingUsers()
    {
        return _userManager.Users
            .Where(u => u.Status == "Pending")
            .Select(u => new PendingUserDto
            {
                Id = u.Id,
                Email = u.Email!,
                Status = u.Status,
                Role = _userManager
                    .GetRolesAsync(u)
                    .Result
                    .FirstOrDefault()
            })
            .ToList();
    }

    public async Task ApproveAsync(
        long bookingId,
        ApprovalStage stage,
        ClaimsPrincipal user)
    {
        var booking = await _db.Bookings
            .Include(b => b.DirectorApprovedByUser)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId);

        if (booking == null)
            throw new InvalidOperationException($"Booking {bookingId} not found");

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? throw new InvalidOperationException("User not authenticated");
        
        var userEmail = user.FindFirst(ClaimTypes.Email)?.Value 
            ?? throw new InvalidOperationException("User email not found in claims");

        var appUser = await _userManager.FindByIdAsync(userId);
        if (appUser == null)
            throw new InvalidOperationException("User not found");

        var userRoles = await _userManager.GetRolesAsync(appUser);
        var userRole = userRoles.FirstOrDefault() ?? "Unknown";

        // Update the appropriate approval
        switch (stage)
        {
            case ApprovalStage.Artist:
                if (booking.ArtistApproval == ApprovalDecision.Approved)
                    throw new InvalidOperationException("Booking already approved by artist");
                
                booking.ArtistApproval = ApprovalDecision.Approved;
                booking.ArtistApprovedAt = DateTime.UtcNow;
                booking.ArtistApprovedByUserId = userId;
                break;

            case ApprovalStage.Location:
                if (booking.LocationApproval == ApprovalDecision.Approved)
                    throw new InvalidOperationException("Booking already approved by location");
                
                booking.LocationApproval = ApprovalDecision.Approved;
                booking.LocationApprovedAt = DateTime.UtcNow;
                booking.LocationApprovedByUserId = userId;
                break;

            case ApprovalStage.Director:
                booking.DirectorApprovedAt = DateTime.UtcNow;
                booking.DirectorApprovedByUserId = userId;
                booking.DirectorApprovedByUser = appUser;
                break;
        }

        // Update overall booking status
        UpdateBookingStatus(booking);

        // Log the action
        var history = new BookingApprovalHistory
        {
            BookingId = bookingId,
            Stage = stage,
            Action = ApprovalAction.Approved,
            ActionAt = DateTime.UtcNow,
            PerformedByUserId = userId,
            PerformedByEmail = userEmail,
            PerformedByRole = userRole
        };

        _db.BookingApprovalHistories.Add(history);
        await _db.SaveChangesAsync();

        // Send approval email to customer
        await SendApprovalEmailAsync(booking, stage, userEmail);
    }

    public async Task RejectAsync(
        long bookingId,
        ApprovalStage stage,
        string reason,
        ClaimsPrincipal user)
    {
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.BookingId == bookingId);

        if (booking == null)
            throw new InvalidOperationException($"Booking {bookingId} not found");

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? throw new InvalidOperationException("User not authenticated");
        
        var userEmail = user.FindFirst(ClaimTypes.Email)?.Value 
            ?? throw new InvalidOperationException("User email not found in claims");

        var appUser = await _userManager.FindByIdAsync(userId);
        if (appUser == null)
            throw new InvalidOperationException("User not found");

        var userRoles = await _userManager.GetRolesAsync(appUser);
        var userRole = userRoles.FirstOrDefault() ?? "Unknown";

        // Set rejection status
        booking.Status = BookingStatus.Rejected;
        booking.RejectionReason = reason;

        // Update the specific stage (mark as rejected)
        switch (stage)
        {
            case ApprovalStage.Artist:
                booking.ArtistApproval = ApprovalDecision.Rejected;
                break;
            case ApprovalStage.Location:
                booking.LocationApproval = ApprovalDecision.Rejected;
                break;
        }

        // Log the action
        var history = new BookingApprovalHistory
        {
            BookingId = bookingId,
            Stage = stage,
            Action = ApprovalAction.Rejected,
            ActionAt = DateTime.UtcNow,
            PerformedByUserId = userId,
            PerformedByEmail = userEmail,
            PerformedByRole = userRole,
            Comment = reason
        };

        _db.BookingApprovalHistories.Add(history);
        await _db.SaveChangesAsync();

        // Send rejection email to customer
        await SendRejectionEmailAsync(booking, stage, reason, userEmail);
    }

    private void UpdateBookingStatus(Booking booking)
    {
        // Determine overall status based on individual approvals
        if (booking.ArtistApproval == ApprovalDecision.Approved 
            && booking.LocationApproval == ApprovalDecision.Approved)
        {
            booking.Status = BookingStatus.FullyApproved;
        }
        else if (booking.ArtistApproval == ApprovalDecision.Approved)
        {
            booking.Status = BookingStatus.ArtistApproved;
        }
        else if (booking.LocationApproval == ApprovalDecision.Approved)
        {
            booking.Status = BookingStatus.LocationApproved;
        }
        else
        {
            booking.Status = BookingStatus.Requested;
        }
    }

    private async Task SendApprovalEmailAsync(Booking booking, ApprovalStage stage, string approverEmail)
    {
        // Get customer email
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == booking.CustomerId);

        if (customer?.Email == null)
            return;

        var stageText = stage switch
        {
            ApprovalStage.Artist => "Artist",
            ApprovalStage.Location => "Location",
            ApprovalStage.Director => "Director",
            _ => "Unknown"
        };

        var html = _templateRenderer.Render("booking_approval", new Dictionary<string, string>
        {
            ["CustomerName"] = customer.FullName,
            ["Stage"] = stageText,
            ["StatusText"] = booking.Status.ToString(),
            ["BookingId"] = booking.BookingId.ToString(),
            ["ApprovedBy"] = approverEmail,
            ["ApprovedAt"] = booking.ArtistApprovedAt?.ToString("MMM d, yyyy h:mm tt") ?? DateTime.UtcNow.ToString("MMM d, yyyy h:mm tt"),
            ["Year"] = DateTime.UtcNow.Year.ToString()
        });

        await _emailSender.SendHtmlAsync(
            customer.Email,
            $"Saqqara Booking Approval - {stageText} Approved",
            html);
    }

    private async Task SendRejectionEmailAsync(Booking booking, ApprovalStage stage, string reason, string rejectorEmail)
    {
        // Get customer email
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == booking.CustomerId);

        if (customer?.Email == null)
            return;

        var stageText = stage switch
        {
            ApprovalStage.Artist => "Artist",
            ApprovalStage.Location => "Location",
            ApprovalStage.Director => "Director",
            _ => "Unknown"
        };

        var html = _templateRenderer.Render("booking_rejection", new Dictionary<string, string>
        {
            ["CustomerName"] = customer.FullName,
            ["Stage"] = stageText,
            ["Reason"] = reason,
            ["BookingId"] = booking.BookingId.ToString(),
            ["RejectedBy"] = rejectorEmail,
            ["RejectedAt"] = DateTime.UtcNow.ToString("MMM d, yyyy h:mm tt"),
            ["Year"] = DateTime.UtcNow.Year.ToString()
        });

        await _emailSender.SendHtmlAsync(
            customer.Email,
            $"Saqqara Booking Status - {stageText} Rejected",
            html);
    }
}

