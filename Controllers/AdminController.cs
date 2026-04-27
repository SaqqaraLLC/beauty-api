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
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserApprovalService _approvalService;
    private readonly BeautyDbContext _db;
    private readonly BlobStorageService _blob;
    private readonly AuditService _audit;
    private readonly EmailTemplateService _email;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        UserApprovalService approvalService,
        BeautyDbContext db,
        BlobStorageService blob,
        AuditService audit,
        EmailTemplateService email)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _approvalService = approvalService;
        _db = db;
        _blob = blob;
        _audit = audit;
        _email = email;
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

    // ── Team Member Invite ───────────────────────────────────────────────
    // Creates a Staff account directly — bypasses registration approval flow.
    // Sends a password-reset email so the invitee sets their own password.

    public record InviteTeamMemberRequest(string Email, string FirstName, string LastName, string Role = "Staff");

    [HttpPost("invite-team-member")]
    public async Task<IActionResult> InviteTeamMember([FromBody] InviteTeamMemberRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "Email is required." });

        var allowedRoles = new[] { "Staff", "Admin" };
        if (!allowedRoles.Contains(req.Role))
            return BadRequest(new { error = "Role must be Staff or Admin." });

        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null)
            return Conflict(new { error = "An account with this email already exists." });

        var user = new ApplicationUser
        {
            UserName  = req.Email,
            Email     = req.Email,
            FirstName = req.FirstName,
            LastName  = req.LastName,
            Status    = "Approved",
            EmailConfirmed = true,
        };

        var tempPassword = Guid.NewGuid().ToString("N")[..12] + "Aa1!";
        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        if (!await _roleManager.RoleExistsAsync(req.Role))
            await _roleManager.CreateAsync(new IdentityRole(req.Role));

        await _userManager.AddToRoleAsync(user, req.Role);

        // Send password reset link so they set their own password
        var token     = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = $"https://saqqarallc.com/auth/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(req.Email)}";

        _ = _email.SendTeamInviteAsync(req.Email, $"{req.FirstName} {req.LastName}".Trim(), resetLink)
              .ContinueWith(_ => { });

        await _audit.LogAsync(ActorId, "Admin.TeamMemberInvited",
            targetEntity: $"User/{user.Id}",
            details: $"Email={req.Email} Role={req.Role}",
            actorEmail: ActorEmail,
            resultCode: 201);

        return StatusCode(201, new { userId = user.Id, email = user.Email, role = req.Role, message = "Invite sent." });
    }

    // ── List All Team Members (Staff + Admin) ─────────────────────────

    [HttpGet("team-members")]
    public async Task<IActionResult> GetTeamMembers()
    {
        var staff = await _userManager.GetUsersInRoleAsync("Staff");
        var admins = await _userManager.GetUsersInRoleAsync("Admin");

        var all = admins.Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Status, Role = "Admin" })
            .Concat(staff.Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Status, Role = "Staff" }))
            .OrderBy(u => u.Role).ThenBy(u => u.LastName);

        return Ok(all);
    }

    // ── Change Role ───────────────────────────────────────────────────────────

    public record ChangeRoleRequest(string Role);

    [HttpPost("change-role/{id}")]
    public async Task<IActionResult> ChangeRole(string id, [FromBody] ChangeRoleRequest req)
    {
        var allowedRoles = new[] { "Staff", "Admin" };
        if (!allowedRoles.Contains(req.Role))
            return BadRequest(new { error = "Role must be Staff or Admin." });

        if (id == ActorId)
            return BadRequest(new { error = "Cannot change your own role." });

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (req.Role != "Admin" && await _userManager.IsInRoleAsync(user, "Admin"))
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
                return BadRequest(new { error = "Cannot remove the last admin." });
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Where(r => allowedRoles.Contains(r)).ToList();
        if (toRemove.Any())
            await _userManager.RemoveFromRolesAsync(user, toRemove);
        await _userManager.AddToRoleAsync(user, req.Role);

        await _audit.LogAsync(ActorId, "Admin.RoleChanged",
            targetEntity: $"User/{id}",
            details: $"Email={user.Email} NewRole={req.Role}",
            actorEmail: ActorEmail,
            resultCode: 200);

        return Ok(new { userId = id, role = req.Role });
    }

    // ── Remove Team Member ────────────────────────────────────────────────────

    [HttpDelete("team-members/{id}")]
    public async Task<IActionResult> RemoveTeamMember(string id)
    {
        if (id == ActorId)
            return BadRequest(new { error = "Cannot remove your own account." });

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
                return BadRequest(new { error = "Cannot remove the last admin." });
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _audit.LogAsync(ActorId, "Admin.TeamMemberRemoved",
            targetEntity: $"User/{id}",
            details: $"Email={user.Email}",
            actorEmail: ActorEmail,
            resultCode: 200);

        return Ok();
    }

    public record RejectDocumentRequest(string Reason);
    public record RejectRequest(string? Reason);
}
