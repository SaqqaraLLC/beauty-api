using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[EnableRateLimiting("general")]
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public ProductsController(BeautyDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private static object MapProduct(Product p, bool includeReviews = false) => new
    {
        productId          = p.ProductId,
        name               = p.Name,
        brand              = p.Brand,
        category           = p.Category,
        description        = p.Description,
        ingredients        = p.Ingredients,
        sku                = p.Sku,
        vendorName         = p.VendorName,
        imageUrl           = p.ImageUrl,
        wholesalePriceCents = p.WholesalePriceCents,
        billedPriceCents   = p.BilledPriceCents,
        status             = p.Status.ToString(),
        isActive           = p.IsActive,
        submittedAt        = p.SubmittedAt,
        approvedAt         = p.ApprovedAt,
        declinedAt         = p.DeclinedAt,
        declineReason      = p.DeclineReason,
        averageRating      = p.AverageRating,
        reviewCount        = p.ReviewCount,
        reviews = includeReviews
            ? p.Reviews.OrderByDescending(r => r.CreatedAt).Select(r => new
            {
                reviewId       = r.ReviewId,
                productId      = r.ProductId,
                reviewerName   = r.ReviewerName,
                reviewerRole   = r.ReviewerRole,
                rating         = r.Rating,
                notes          = r.Notes,
                recommendation = r.Recommendation,
                createdAt      = r.CreatedAt
            }).ToList<object>()
            : new List<object>()
    };

    // ── GET /api/products ───────────────────────────────────────────
    // Admin sees all; others see only Approved + their own submissions

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? vendor)
    {
        var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Admin");

        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Reviews)
            .AsQueryable();

        if (!isAdmin)
        {
            query = query.Where(p =>
                p.Status == ProductStatus.Approved ||
                p.SubmittedByUserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<ProductStatus>(status, true, out var parsedStatus))
            query = query.Where(p => p.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        if (!string.IsNullOrWhiteSpace(vendor))
            query = query.Where(p => p.VendorName != null && p.VendorName.Contains(vendor));

        var products = await query
            .OrderByDescending(p => p.SubmittedAt)
            .ToListAsync();

        return Ok(products.Select(p => MapProduct(p, includeReviews: true)));
    }

    // ── GET /api/products/{id} ──────────────────────────────────────

    [HttpGet("{id:int}")]
    [Authorize]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync(p => p.ProductId == id);

        if (product is null) return NotFound();

        return Ok(MapProduct(product, includeReviews: true));
    }

    // ── POST /api/products ──────────────────────────────────────────
    // Any authenticated user can submit a product for review

    public record CreateProductReq(
        string Name,
        string Brand,
        string? Category,
        string? Description,
        string? Ingredients,
        string? Sku,
        string? VendorName,
        int WholesalePriceCents);

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateProductReq req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user   = await _users.FindByIdAsync(userId);

        var product = new Product
        {
            Name                = req.Name,
            Brand               = req.Brand,
            Category            = req.Category ?? "Other",
            Description         = req.Description,
            Ingredients         = req.Ingredients,
            Sku                 = req.Sku,
            VendorName          = req.VendorName ?? "%PURE",
            WholesalePriceCents = req.WholesalePriceCents,
            SubmittedByUserId   = userId,
            Status              = ProductStatus.Pending,
            SubmittedAt         = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.ProductId },
            MapProduct(product));
    }

    // ── POST /api/products/{id}/status ─────────────────────────────
    // Admin: approve or decline

    public record UpdateStatusReq(string Status, string? DeclineReason);

    [HttpPost("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusReq req)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();

        if (!Enum.TryParse<ProductStatus>(req.Status, true, out var newStatus))
            return BadRequest(new { message = "Invalid status. Use Approved or Declined." });

        product.Status = newStatus;

        if (newStatus == ProductStatus.Approved)
            product.ApprovedAt = DateTime.UtcNow;
        else if (newStatus == ProductStatus.Declined)
        {
            product.DeclinedAt    = DateTime.UtcNow;
            product.DeclineReason = req.DeclineReason;
            product.IsActive      = false;
        }

        await _db.SaveChangesAsync();

        return Ok(new { productId = id, status = newStatus.ToString() });
    }

    // ── POST /api/products/{id}/reviews ────────────────────────────
    // Admin / Staff submit an internal product assessment

    public record CreateProductReviewReq(
        int Rating,
        string? Notes,
        string Recommendation);

    [HttpPost("{id:int}/reviews")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> AddReview(
        int id, [FromBody] CreateProductReviewReq req)
    {
        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { message = "Rating must be 1–5." });

        var validRecs = new[] { "Approve", "Decline", "Neutral" };
        if (!validRecs.Contains(req.Recommendation))
            return BadRequest(new { message = "Recommendation must be Approve, Decline, or Neutral." });

        var product = await _db.Products
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync(p => p.ProductId == id);

        if (product is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user   = await _users.FindByIdAsync(userId);
        var roles  = await _users.GetRolesAsync(user!);

        var review = new ProductReview
        {
            ProductId      = id,
            ReviewerUserId = userId,
            ReviewerName   = $"{user?.FirstName} {user?.LastName}".Trim(),
            ReviewerRole   = roles.FirstOrDefault() ?? "Staff",
            Rating         = req.Rating,
            Notes          = req.Notes,
            Recommendation = req.Recommendation,
            CreatedAt      = DateTime.UtcNow
        };

        _db.ProductReviews.Add(review);

        // Recalculate average
        product.Reviews.Add(review);
        product.ReviewCount   = product.Reviews.Count;
        product.AverageRating = Math.Round(product.Reviews.Average(r => r.Rating), 2);

        await _db.SaveChangesAsync();

        return Ok(new
        {
            reviewId       = review.ReviewId,
            productId      = review.ProductId,
            reviewerName   = review.ReviewerName,
            reviewerRole   = review.ReviewerRole,
            rating         = review.Rating,
            notes          = review.Notes,
            recommendation = review.Recommendation,
            createdAt      = review.CreatedAt
        });
    }

    // ── GET /api/products/approved ──────────────────────────────────
    // Lightweight list used by invoice creation to pick kit items

    [HttpGet("approved")]
    [Authorize]
    public async Task<IActionResult> GetApproved([FromQuery] string? vendor)
    {
        var query = _db.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Approved && p.IsActive);

        if (!string.IsNullOrWhiteSpace(vendor))
            query = query.Where(p => p.VendorName != null && p.VendorName.Contains(vendor));

        var products = await query
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToListAsync();

        return Ok(products.Select(p => new
        {
            productId          = p.ProductId,
            name               = p.Name,
            brand              = p.Brand,
            category           = p.Category,
            vendorName         = p.VendorName,
            wholesalePriceCents = p.WholesalePriceCents,
            billedPriceCents   = p.BilledPriceCents
        }));
    }
}
