using Beauty.Api.Domain.Broadcasting;
using Beauty.Api.Models.Broadcasting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/broadcasts")]
[Authorize(Roles = "Admin")]
public class BroadcastController : ControllerBase
{
    private readonly IBroadcastService _broadcastService;
    private readonly ILogger<BroadcastController> _logger;

    public BroadcastController(IBroadcastService broadcastService, ILogger<BroadcastController> logger)
    {
        _broadcastService = broadcastService;
        _logger = logger;
    }

    // ============================
    // REQUEST/RESPONSE MODELS
    // ============================

    public record CreateCampaignRequest(
        string Title,
        string Subject,
        string Body,
        string Description,
        BroadcastChannelType Channel,
        long? SegmentId);

    public record ScheduleCampaignRequest(DateTime ScheduledFor);

    public record CreateSegmentRequest(
        string Name,
        BroadcastTargetRole? TargetRole,
        string? LocationIds,
        string? ArtistIds,
        int? BookedWithinDays,
        bool IncludeInactive);

    // ============================
    // CAMPAIGNS
    // ============================

    /// <summary>Create a new broadcast campaign (Draft status)</summary>
    [HttpPost("campaigns")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCampaign([FromBody] CreateCampaignRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var campaign = await _broadcastService.CreateCampaignAsync(
            request.Title,
            request.Subject,
            request.Body,
            request.Channel,
            request.SegmentId,
            request.Description,
            User);

        return Created($"/api/broadcasts/campaigns/{campaign.CampaignId}", new
        {
            campaign.CampaignId,
            campaign.Title,
            campaign.Status,
            campaign.CreatedAt
        });
    }

    /// <summary>Get all campaigns with pagination</summary>
    [HttpGet("campaigns")]
    public async Task<IActionResult> GetAllCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var campaigns = await _broadcastService.GetAllCampaignsAsync(page, pageSize);
        return Ok(campaigns.Select(c => new
        {
            c.CampaignId,
            c.Title,
            c.Subject,
            c.Status,
            c.Channel,
            c.CreatedAt,
            c.ScheduledFor,
            c.SentAt,
            RecipientCount = c.Recipients.Count
        }));
    }

    /// <summary>Get campaign details</summary>
    [HttpGet("campaigns/{id:long}")]
    public async Task<IActionResult> GetCampaign(long id)
    {
        var campaign = await _broadcastService.GetCampaignAsync(id);
        if (campaign == null)
            return NotFound();

        return Ok(new
        {
            campaign.CampaignId,
            campaign.Title,
            campaign.Subject,
            campaign.Body,
            campaign.Description,
            campaign.Status,
            campaign.Channel,
            campaign.CreatedAt,
            campaign.ScheduledFor,
            campaign.SentAt,
            campaign.SegmentId,
            RecipientCount = campaign.Recipients.Count
        });
    }

    /// <summary>Schedule a campaign for later sending</summary>
    [HttpPost("campaigns/{id:long}/schedule")]
    public async Task<IActionResult> ScheduleCampaign(long id, [FromBody] ScheduleCampaignRequest request)
    {
        try
        {
            var campaign = await _broadcastService.ScheduleCampaignAsync(id, request.ScheduledFor, User);
            return Ok(new { campaign.CampaignId, campaign.Status, campaign.ScheduledFor });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Send campaign immediately</summary>
    [HttpPost("campaigns/{id:long}/send")]
    public async Task<IActionResult> SendCampaignNow(long id)
    {
        try
        {
            var campaign = await _broadcastService.SendCampaignNowAsync(id, User);
            return Ok(new
            {
                campaign.CampaignId,
                campaign.Status,
                campaign.SentAt,
                RecipientCount = campaign.Recipients.Count
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Cancel a scheduled or draft campaign</summary>
    [HttpPost("campaigns/{id:long}/cancel")]
    public async Task<IActionResult> CancelCampaign(long id)
    {
        try
        {
            await _broadcastService.CancelCampaignAsync(id, User);
            return Ok(new { message = "Campaign cancelled" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Get campaign recipients and delivery status</summary>
    [HttpGet("campaigns/{id:long}/recipients")]
    public async Task<IActionResult> GetCampaignRecipients(long id)
    {
        var recipients = await _broadcastService.GetCampaignRecipientsAsync(id);
        return Ok(recipients.Select(r => new
        {
            r.RecipientId,
            r.RecipientEmail,
            r.Status,
            r.SentAt,
            r.OpenedAt,
            r.ClickedAt,
            r.ErrorMessage
        }));
    }

    // ============================
    // SEGMENTS
    // ============================

    /// <summary>Create an audience segment</summary>
    [HttpPost("segments")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSegment([FromBody] CreateSegmentRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var segment = await _broadcastService.CreateSegmentAsync(
            request.Name,
            request.TargetRole,
            request.LocationIds,
            request.ArtistIds,
            request.BookedWithinDays,
            request.IncludeInactive);

        return Created($"/api/broadcasts/segments/{segment.SegmentId}", new
        {
            segment.SegmentId,
            segment.Name,
            segment.EstimatedRecipientCount
        });
    }

    /// <summary>Get recipients for a segment</summary>
    [HttpGet("segments/{id:long}/recipients")]
    public async Task<IActionResult> GetSegmentRecipients(long id)
    {
        try
        {
            var recipients = await _broadcastService.GetSegmentRecipientsAsync(id);
            return Ok(new
            {
                recipientCount = recipients.Count,
                recipients
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting segment recipients: {ex.Message}");
            return BadRequest(new { error = ex.Message });
        }
    }
}
