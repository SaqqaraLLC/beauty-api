using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[Authorize(Roles = "Admin")]
[EnableRateLimiting("general")]
[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserApprovalService _approvalService;
    private readonly BeautyDbContext _db;
    private readonly BlobStorageService _blob;
    private readonly AuditService _audit;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        UserApprovalService approvalService,
        BeautyDbContext db,
        BlobStorageService blob,
        AuditService audit)
    {
        _userManager = userManager;
        _approvalService = approvalService;
        _db = db;
        _blob = blob;
        _audit = audit;
    }

    private string ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    private string ActorEmail => User.FindFirstValue(ClaimTypes.Email) ?? "";

    // ── User Approval ────────────────────────────────────────────────

    [HttpGet("pending-users")]
    public async Task<IActionResult> GetPendingUsers()
    {
        var users = await _userManager.Users
            .Where(u => u.Status == "Pending")
            .ToListAsync();

        var result = new List<object>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(new
            {
                u.Id,
                u.Email,
                u.Status,
                Role = roles.FirstOrDefault() ?? ""
            });
        }

        return Ok(result);
    }

    [HttpPost("approve/{id}")]
    public async Task<IActionResult> Approve(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        await _approvalService.ApproveUserAsync(user, ActorId);

        await _audit.LogAsync(ActorId, "Admin.UserApproved",
            targetEntity: $"User/{id}",
            details: $"Email={user.Email}",
            actorEmail: ActorEmail,
            resultCode: 200);

        return Ok();
    }

    [HttpPost("reject/{id}")]
    public async Task<IActionResult> Reject(string id, [FromBody] RejectRequest body)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        await _approvalService.RejectUserAsync(user, ActorId, body.Reason ?? string.Empty);

        await _audit.LogAsync(ActorId, "Admin.UserRejected",
            targetEntity: $"User/{id}",
            details: $"Email={user.Email} Reason={body.Reason}",
            actorEmail: ActorEmail,
            resultCode: 200);

        return Ok();
    }

    // ── Document Verification ─────────────────────────────────────────

    [HttpGet("documents")]
    public async Task<IActionResult> GetAllDocuments(
        [FromQuery] string? status,
        [FromQuery] string? ownerType)
    {
        var query = _db.UserDocuments.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(ownerType))
            query = query.Where(d => d.OwnerType == ownerType);

        var docs = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();

        var userIds = docs.Select(d => d.UserId).Distinct().ToList();
        var users = await _userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync();
        var userMap = users.ToDictionary(u => u.Id, u => u.Email);

        return Ok(docs.Select(d => new
        {
            id = d.Id,
            ownerType = d.OwnerType,
            ownerName = userMap.GetValueOrDefault(d.UserId),
            documentType = d.DocumentType,
            documentName = d.DocumentName,
            documentNumber = d.DocumentNumber,
            expiresAt = d.ExpiresAt,
            status = d.Status,
            rejectionReason = d.RejectionReason,
            createdAt = d.CreatedAt
        }));
    }

    [HttpPost("documents/{id}/verify")]
    public async Task<IActionResult> VerifyDocument(string id)
    {
        var doc = await _db.UserDocuments.FindAsync(id);
        if (doc == null) return NotFound();

        doc.Status = "Verified";
        doc.ReviewedAt = DateTime.UtcNow;
        doc.ReviewedByUserId = ActorId;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(ActorId, "Admin.DocumentVerified",
            targetEntity: $"Document/{id}",
            details: $"Type={doc.DocumentType}",
            actorEmail: ActorEmail,
            resultCode: 200);

        return Ok(new { doc.Id, doc.Status });
    }

    [HttpPost("documents/{id}/reject")]
    public async Task<IActionResult> RejectDocument(string id, [FromBody] RejectDocumentRequest req)
    {
        var doc = await _db.UserDocuments.FindAsync(id);
        if (doc == null) return NotFound();

        doc.Status = "Rejected";
        doc.RejectionReason = req.Reason;
        doc.ReviewedAt = DateTime.UtcNow;
        doc.ReviewedByUserId = ActorId;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(ActorId, "Admin.DocumentRejected",
            targetEntity: $"Document/{id}",
            details: $"Type={doc.DocumentType} Reason={req.Reason}",
            actorEmail: ActorEmail,
            resultCode: 200);

        return Ok(new { doc.Id, doc.Status });
    }

    [HttpGet("documents/{id}/view")]
    public async Task<IActionResult> ViewDocument(string id)
    {
        var doc = await _db.UserDocuments.FindAsync(id);
        if (doc == null) return NotFound();
        if (doc.FileUrl == null) return NotFound(new { message = "No file attached to this document." });

        await _audit.LogAsync(ActorId, "Admin.DocumentViewed",
            targetEntity: $"Document/{id}",
            actorEmail: ActorEmail,
            resultCode: 200);

        var sasUrl = _blob.GenerateSasUrl(doc.FileUrl, expiryMinutes: 60);
        return Ok(new { url = sasUrl, expiresInMinutes = 60 });
    }

    // ── Audit Log Viewer ─────────────────────────────────────────────

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] string? actorEmail,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(actorEmail))
            query = query.Where(l => l.ActorEmail != null && l.ActorEmail.Contains(actorEmail));
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action.Contains(action));
        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);

        var total = await query.CountAsync();
        var logs  = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            items = logs.Select(l => new
            {
                id           = l.Id,
                actorEmail   = l.ActorEmail,
                actorUserId  = l.ActorUserId,
                action       = l.Action,
                targetEntity = l.TargetEntity,
                details      = l.Details,
                ipAddress    = l.IpAddress,
                resultCode   = l.ResultCode,
                timestamp    = l.Timestamp,
            })
        });
    }

    public record RejectDocumentRequest(string Reason);
    public record RejectRequest(string? Reason);
}
