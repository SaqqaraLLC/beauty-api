using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public DocumentsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    // GET /api/documents?ownerType=Artist
    [HttpGet]
    public async Task<IActionResult> GetMyDocuments([FromQuery] string? ownerType)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var query = _db.UserDocuments.Where(d => d.UserId == userId);
        if (!string.IsNullOrWhiteSpace(ownerType))
            query = query.Where(d => d.OwnerType == ownerType);

        var docs = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
        return Ok(docs.Select(MapDoc));
    }

    // POST /api/documents
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitDocumentRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var doc = new UserDocument
        {
            UserId = userId,
            OwnerType = req.OwnerType,
            DocumentType = req.DocumentType,
            DocumentName = req.DocumentName,
            DocumentNumber = req.DocumentNumber,
            ExpiresAt = req.ExpiresAt.HasValue ? req.ExpiresAt.Value.ToUniversalTime() : null,
            Status = "Pending"
        };

        _db.UserDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return Ok(MapDoc(doc));
    }

    private static object MapDoc(UserDocument d) => new
    {
        id = d.Id,
        documentType = d.DocumentType,
        documentName = d.DocumentName,
        documentNumber = d.DocumentNumber,
        expiresAt = d.ExpiresAt,
        status = d.Status,
        rejectionReason = d.RejectionReason,
        createdAt = d.CreatedAt
    };

    public record SubmitDocumentRequest(
        string OwnerType,
        string DocumentType,
        string DocumentName,
        string? DocumentNumber,
        DateTime? ExpiresAt);
}
