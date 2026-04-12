using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Beauty.Api.Services;

namespace Beauty.Api.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserApprovalService _approvalService;
    private readonly BeautyDbContext _db;
    private readonly BlobStorageService _blob;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        UserApprovalService approvalService,
        BeautyDbContext db,
        BlobStorageService blob)
    {
        _userManager = userManager;
        _approvalService = approvalService;
        _db = db;
        _blob = blob;
    }

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
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        await _approvalService.ApproveUserAsync(user, adminId!);
        return Ok();
    }

    [HttpPost("reject/{id}")]
    public async Task<IActionResult> Reject(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        user.Status = "Rejected";
        await _userManager.UpdateAsync(user);
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

        // Fetch owner emails
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

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        doc.Status = "Verified";
        doc.ReviewedAt = DateTime.UtcNow;
        doc.ReviewedByUserId = adminId;

        await _db.SaveChangesAsync();
        return Ok(new { doc.Id, doc.Status });
    }

    [HttpPost("documents/{id}/reject")]
    public async Task<IActionResult> RejectDocument(string id, [FromBody] RejectDocumentRequest req)
    {
        var doc = await _db.UserDocuments.FindAsync(id);
        if (doc == null) return NotFound();

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        doc.Status = "Rejected";
        doc.RejectionReason = req.Reason;
        doc.ReviewedAt = DateTime.UtcNow;
        doc.ReviewedByUserId = adminId;

        await _db.SaveChangesAsync();
        return Ok(new { doc.Id, doc.Status });
    }

    // GET /admin/documents/{id}/view  — returns a 1-hour SAS URL for the file
    [HttpGet("documents/{id}/view")]
    public async Task<IActionResult> ViewDocument(string id)
    {
        var doc = await _db.UserDocuments.FindAsync(id);
        if (doc == null) return NotFound();
        if (doc.FileUrl == null) return NotFound(new { message = "No file attached to this document." });

        var sasUrl = _blob.GenerateSasUrl(doc.FileUrl, expiryMinutes: 60);
        return Ok(new { url = sasUrl, expiresInMinutes = 60 });
    }

    public record RejectDocumentRequest(string Reason);
}
