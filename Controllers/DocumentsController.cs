using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[EnableRateLimiting("general")]
[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly BlobStorageService _blob;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".pdf", ".heic" };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public DocumentsController(BeautyDbContext db, UserManager<ApplicationUser> users, BlobStorageService blob)
    {
        _db = db;
        _users = users;
        _blob = blob;
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

    // POST /api/documents/upload  (multipart/form-data)
    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] string ownerType,
        [FromForm] string documentType,
        [FromForm] string documentName,
        [FromForm] string? documentNumber,
        [FromForm] string? expiresAt,
        IFormFile? file)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        string? fileUrl = null;

        if (file != null)
        {
            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { code = "FILE_TOO_LARGE", message = "Maximum file size is 10 MB." });

            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(new { code = "INVALID_FILE_TYPE", message = "Allowed: JPG, PNG, PDF, HEIC." });

            using var stream = file.OpenReadStream();
            var blobName = await _blob.UploadAsync(stream, file.FileName, file.ContentType);
            fileUrl = blobName; // store blob name, not public URL
        }

        DateTime? expiry = null;
        if (!string.IsNullOrWhiteSpace(expiresAt) && DateTime.TryParse(expiresAt, out var parsed))
            expiry = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        var doc = new UserDocument
        {
            UserId = userId,
            OwnerType = ownerType,
            DocumentType = documentType,
            DocumentName = documentName,
            DocumentNumber = documentNumber,
            ExpiresAt = expiry,
            FileUrl = fileUrl,
            Status = "Pending"
        };

        _db.UserDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return Ok(MapDoc(doc));
    }

    // POST /api/documents  (metadata only — kept for backwards compat)
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitDocumentRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        DateTime? expiry = req.ExpiresAt.HasValue
            ? DateTime.SpecifyKind(req.ExpiresAt.Value, DateTimeKind.Utc)
            : null;

        var doc = new UserDocument
        {
            UserId = userId,
            OwnerType = req.OwnerType,
            DocumentType = req.DocumentType,
            DocumentName = req.DocumentName,
            DocumentNumber = req.DocumentNumber,
            ExpiresAt = expiry,
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
        hasFile = d.FileUrl != null,
        createdAt = d.CreatedAt
    };

    public record SubmitDocumentRequest(
        string OwnerType,
        string DocumentType,
        string DocumentName,
        string? DocumentNumber,
        DateTime? ExpiresAt);
}
