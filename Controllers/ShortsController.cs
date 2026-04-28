using Beauty.Api.Data;
using Beauty.Api.Models.Gifts;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/shorts")]
public class ShortsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly BlobStorageService _blob;
    private readonly ILogger<ShortsController> _logger;

    public ShortsController(BeautyDbContext db, BlobStorageService blob, ILogger<ShortsController> logger)
    {
        _db     = db;
        _blob   = blob;
        _logger = logger;
    }

    // ── GET /api/shorts ───────────────────────────────────────────────────────
    // Public: browse latest shorts (homepage + /shorts page)

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Browse([FromQuery] int limit = 20, [FromQuery] int offset = 0)
    {
        var shorts = await _db.ArtistShorts
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(offset)
            .Take(Math.Min(limit, 50))
            .Select(s => new
            {
                s.Id,
                s.ArtistUserId,
                s.ArtistName,
                s.Title,
                s.VideoUrl,
                s.ThumbnailUrl,
                s.DurationSeconds,
                s.Views,
                s.Likes,
                s.CreatedAt,
            })
            .ToListAsync();

        return Ok(shorts);
    }

    // ── POST /api/shorts/upload-url ───────────────────────────────────────────
    // Artist: get a pre-signed blob URL to upload their short video

    [HttpPost("upload-url")]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> GetUploadUrl([FromBody] UploadUrlRequest req)
    {
        var ext      = Path.GetExtension(req.FileName).ToLowerInvariant();
        var allowed  = new[] { ".mp4", ".mov", ".webm" };
        if (!allowed.Contains(ext))
            return BadRequest(new { error = "Only mp4, mov, and webm files are supported." });

        var blobName = $"shorts/{Guid.NewGuid()}{ext}";
        var uploadUrl = await _blob.GenerateUploadSasUrlAsync(blobName, req.ContentType);

        return Ok(new { uploadUrl, blobName });
    }

    // ── POST /api/shorts ──────────────────────────────────────────────────────
    // Artist: publish a short after uploading the video blob

    [HttpPost]
    [Authorize(Roles = "Artist")]
    public async Task<IActionResult> Publish([FromBody] PublishShortRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var name   = User.FindFirstValue(ClaimTypes.Name)
                  ?? User.FindFirstValue("name")
                  ?? "Artist";

        if (string.IsNullOrWhiteSpace(req.VideoUrl))
            return BadRequest(new { error = "Video URL is required." });

        var videoUrl   = _blob.GetPublicUrl(req.BlobName);
        var thumbUrl   = string.IsNullOrWhiteSpace(req.ThumbnailBlobName)
                            ? null
                            : _blob.GetPublicUrl(req.ThumbnailBlobName);

        var s = new ArtistShort
        {
            ArtistUserId    = userId,
            ArtistName      = name,
            Title           = req.Title.Trim(),
            VideoUrl        = videoUrl,
            ThumbnailUrl    = thumbUrl,
            DurationSeconds = req.DurationSeconds,
            IsActive        = true,
            CreatedAt       = DateTime.UtcNow,
        };

        _db.ArtistShorts.Add(s);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[SHORTS] Artist {UserId} published short {ShortId}", userId, s.Id);
        return Ok(new { shortId = s.Id });
    }

    // ── POST /api/shorts/{id}/view ────────────────────────────────────────────

    [HttpPost("{id:long}/view")]
    [AllowAnonymous]
    public async Task<IActionResult> RecordView(long id)
    {
        await _db.ArtistShorts
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Views, x => x.Views + 1));
        return Ok();
    }

    // ── POST /api/shorts/{id}/like ────────────────────────────────────────────

    [HttpPost("{id:long}/like")]
    [Authorize]
    public async Task<IActionResult> Like(long id)
    {
        await _db.ArtistShorts
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Likes, x => x.Likes + 1));
        return Ok();
    }

    // ── DELETE /api/shorts/{id} ───────────────────────────────────────────────

    [HttpDelete("{id:long}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var roles  = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var isAdmin = roles.Contains("Admin") || roles.Contains("Staff");

        var s = await _db.ArtistShorts.FindAsync(id);
        if (s == null) return NotFound();
        if (!isAdmin && s.ArtistUserId != userId) return Forbid();

        s.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record UploadUrlRequest(string FileName, string ContentType);
    public record PublishShortRequest(string Title, string BlobName, string VideoUrl, string? ThumbnailBlobName, int DurationSeconds);
}
