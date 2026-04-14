using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[EnableRateLimiting("general")]
[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public ReviewsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    // ── GET /api/reviews?entityType=Artist&entityId=5 ───────────────

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetReviews(
        [FromQuery] string entityType,
        [FromQuery] int entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return BadRequest(new { message = "entityType is required." });

        var reviews = await _db.Reviews
            .AsNoTracking()
            .Where(r =>
                r.SubjectEntityType == entityType &&
                r.SubjectEntityId == entityId &&
                r.Status == "Published")
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(reviews.Select(r => new
        {
            reviewId = r.ReviewId,
            bookingId = r.BookingId,
            reviewerUserId = r.ReviewerUserId,
            reviewerRole = r.ReviewerRole,
            reviewerName = r.ReviewerName,
            reviewerAvatarUrl = r.ReviewerAvatarUrl,
            subjectEntityType = r.SubjectEntityType,
            subjectEntityId = r.SubjectEntityId,
            subjectName = r.SubjectName,
            rating = r.Rating,
            title = r.Title,
            body = r.Body,
            isVerifiedBooking = r.IsVerifiedBooking,
            status = r.Status,
            createdAt = r.CreatedAt
        }));
    }

    // ── GET /api/reviews/summary?entityType=Artist&entityId=5 ───────

    [HttpGet("summary")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string entityType,
        [FromQuery] int entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return BadRequest(new { message = "entityType is required." });

        var ratings = await _db.Reviews
            .AsNoTracking()
            .Where(r =>
                r.SubjectEntityType == entityType &&
                r.SubjectEntityId == entityId &&
                r.Status == "Published")
            .Select(r => r.Rating)
            .ToListAsync();

        var total = ratings.Count;
        var average = total > 0 ? Math.Round(ratings.Average(), 2) : 0.0;

        return Ok(new
        {
            entityType,
            entityId,
            averageRating = average,
            totalReviews = total,
            breakdown = new
            {
                stars5 = ratings.Count(r => r == 5),
                stars4 = ratings.Count(r => r == 4),
                stars3 = ratings.Count(r => r == 3),
                stars2 = ratings.Count(r => r == 2),
                stars1 = ratings.Count(r => r == 1)
            }
        });
    }

    // ── POST /api/reviews ───────────────────────────────────────────

    public record CreateReviewReq(
        string SubjectEntityType,
        int SubjectEntityId,
        string SubjectName,
        int Rating,
        string? Title,
        string? Body,
        int? BookingId);

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateReviewReq req)
    {
        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { message = "Rating must be between 1 and 5." });

        var validEntityTypes = new[] { "Artist", "Client", "Company", "Agent" };
        if (!validEntityTypes.Contains(req.SubjectEntityType))
            return BadRequest(new { message = "Invalid subjectEntityType." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        // Determine reviewer role from claims
        var roles = await _users.GetRolesAsync(user);
        var reviewerRole = roles.FirstOrDefault() ?? "Client";

        // Verify booking ownership if bookingId provided
        bool isVerifiedBooking = false;
        if (req.BookingId.HasValue)
        {
            isVerifiedBooking = await _db.Bookings
                .AnyAsync(b =>
                    b.BookingId == req.BookingId.Value &&
                    b.CustomerId.ToString() == userId);
        }

        var review = new Review
        {
            BookingId = req.BookingId,
            ReviewerUserId = userId,
            ReviewerRole = reviewerRole,
            ReviewerName = $"{user.FirstName} {user.LastName}".Trim(),
            SubjectEntityType = req.SubjectEntityType,
            SubjectEntityId = req.SubjectEntityId,
            SubjectName = req.SubjectName,
            Rating = req.Rating,
            Title = req.Title,
            Body = req.Body,
            IsVerifiedBooking = isVerifiedBooking,
            Status = "Published",
            CreatedAt = DateTime.UtcNow
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        // ── Recalculate averages on the subject entity ───────────────
        await RecalculateRatingsAsync(req.SubjectEntityType, req.SubjectEntityId);

        return CreatedAtAction(nameof(GetReviews),
            new { entityType = req.SubjectEntityType, entityId = req.SubjectEntityId },
            new
            {
                reviewId = review.ReviewId,
                bookingId = review.BookingId,
                reviewerUserId = review.ReviewerUserId,
                reviewerRole = review.ReviewerRole,
                reviewerName = review.ReviewerName,
                subjectEntityType = review.SubjectEntityType,
                subjectEntityId = review.SubjectEntityId,
                subjectName = review.SubjectName,
                rating = review.Rating,
                title = review.Title,
                body = review.Body,
                isVerifiedBooking = review.IsVerifiedBooking,
                status = review.Status,
                createdAt = review.CreatedAt
            });
    }

    // ── GET /api/reviews/admin ──────────────────────────────────────
    // Admin: list all reviews across all statuses with optional filters

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminGetAll(
        [FromQuery] string? status,
        [FromQuery] string? entityType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.Reviews.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(r => r.SubjectEntityType == entityType);

        var total = await query.CountAsync();

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page, pageSize, total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = reviews.Select(r => new
            {
                reviewId          = r.ReviewId,
                bookingId         = r.BookingId,
                reviewerUserId    = r.ReviewerUserId,
                reviewerRole      = r.ReviewerRole,
                reviewerName      = r.ReviewerName,
                reviewerAvatarUrl = r.ReviewerAvatarUrl,
                subjectEntityType = r.SubjectEntityType,
                subjectEntityId   = r.SubjectEntityId,
                subjectName       = r.SubjectName,
                rating            = r.Rating,
                title             = r.Title,
                body              = r.Body,
                isVerifiedBooking = r.IsVerifiedBooking,
                status            = r.Status,
                createdAt         = r.CreatedAt
            })
        });
    }

    // ── POST /api/reviews/{id}/publish ─────────────────────────────

    [HttpPost("{id:int}/publish")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Publish(int id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review is null) return NotFound();

        var prev = review.Status;
        review.Status = "Published";
        await _db.SaveChangesAsync();

        // Recalculate if transitioning from non-published
        if (prev != "Published")
            await RecalculateRatingsAsync(review.SubjectEntityType, review.SubjectEntityId);

        return Ok(new { reviewId = id, status = "Published" });
    }

    // ── POST /api/reviews/{id}/remove ──────────────────────────────

    [HttpPost("{id:int}/remove")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Remove(int id, [FromBody] RemoveReviewReq? body)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review is null) return NotFound();

        var prev = review.Status;
        review.Status = "Removed";
        await _db.SaveChangesAsync();

        // Recalculate if was published
        if (prev == "Published")
            await RecalculateRatingsAsync(review.SubjectEntityType, review.SubjectEntityId);

        return Ok(new { reviewId = id, status = "Removed" });
    }

    public record RemoveReviewReq(string? Reason);

    // ── Recalculate helper ───────────────────────────────────────────

    private async Task RecalculateRatingsAsync(string entityType, int entityId)
    {
        var ratings = await _db.Reviews
            .AsNoTracking()
            .Where(r =>
                r.SubjectEntityType == entityType &&
                r.SubjectEntityId == entityId &&
                r.Status == "Published")
            .Select(r => r.Rating)
            .ToListAsync();

        var count = ratings.Count;
        var average = count > 0 ? Math.Round(ratings.Average(), 2) : 0.0;

        if (entityType == "Artist")
        {
            var profile = await _db.ArtistProfiles.FindAsync(entityId);
            if (profile is not null)
            {
                profile.AverageRating = average;
                profile.ReviewCount = count;
                await _db.SaveChangesAsync();
            }
        }
        else if (entityType == "Agent")
        {
            var profile = await _db.AgentProfiles.FindAsync(entityId);
            if (profile is not null)
            {
                profile.AverageRating = average;
                profile.ReviewCount = count;
                await _db.SaveChangesAsync();
            }
        }
    }
}
