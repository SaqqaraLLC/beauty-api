using Beauty.Api.Data;
using Beauty.Api.Models.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

// ────────────────────────────────────────────────────────────────────────────
// Products — Admin & Artist endpoints
// ────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(BeautyDbContext db, ILogger<ProductsController> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── List — artists get Approved only; admins get all ─────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? category)
    {
        var isAdmin = User.IsInRole("Admin");

        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Reviews)
            .AsQueryable();

        // Non-admins always see only Approved
        if (!isAdmin)
            query = query.Where(p => p.Status == ProductStatus.Approved);
        else if (!string.IsNullOrEmpty(status) &&
                 Enum.TryParse<ProductStatus>(status, ignoreCase: true, out var s))
            query = query.Where(p => p.Status == s);

        if (!string.IsNullOrEmpty(category) && category != "All")
            query = query.Where(p => p.Category == category);

        var products = await query
            .OrderByDescending(p => p.SubmittedAt)
            .Select(p => new
            {
                p.ProductId,
                p.Name,
                p.Brand,
                p.Category,
                p.Description,
                p.Ingredients,
                p.VendorName,
                p.Sku,
                p.ImageUrl,
                p.WholesalePriceCents,
                p.BilledPriceCents,
                p.PromoBilledPriceCents,
                p.Status,
                p.DeclineReason,
                p.SubmittedBy,
                p.SubmittedAt,
                p.ReviewedAt,
                AverageRating = p.Reviews.Any() ? p.Reviews.Average(r => (double)r.Rating) : (double?)null,
                ReviewCount   = p.Reviews.Count,
                Reviews = isAdmin ? p.Reviews.Select(r => new
                {
                    r.ReviewId,
                    r.ReviewerName,
                    r.Rating,
                    r.Notes,
                    r.Recommendation,
                    r.ReviewedAt
                }) : null
            })
            .ToListAsync();

        return Ok(products);
    }

    // ── Submit (Admin adds directly) ──────────────────────────────────────────

    public record CreateProductRequest(
        string Name,
        string Brand,
        string Category,
        string? Description,
        string? Ingredients,
        string? VendorName,
        string? Sku,
        string? ImageUrl,
        long WholesalePriceCents);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest req)
    {
        var wholesale = req.WholesalePriceCents;
        var product = new Product
        {
            Name                  = req.Name,
            Brand                 = req.Brand,
            Category              = req.Category,
            Description           = req.Description,
            Ingredients           = req.Ingredients,
            VendorName            = req.VendorName,
            Sku                   = req.Sku,
            ImageUrl              = req.ImageUrl,
            WholesalePriceCents   = wholesale,
            BilledPriceCents      = (long)(wholesale * 1.8),
            PromoBilledPriceCents = (long)(wholesale * 1.6),
            Status                = ProductStatus.Pending,
            SubmittedBy           = "admin",
            SubmittedAt           = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return Ok(new { product.ProductId, product.Status });
    }

    // ── Update status (Admin approve / decline) ───────────────────────────────

    public record StatusRequest(string Status, string? DeclineReason);

    [HttpPost("{id:long}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] StatusRequest req)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();

        if (!Enum.TryParse<ProductStatus>(req.Status, ignoreCase: true, out var s))
            return BadRequest(new { error = "Invalid status" });

        product.Status          = s;
        product.DeclineReason   = req.DeclineReason;
        product.ReviewedAt      = DateTime.UtcNow;
        product.ReviewedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        await _db.SaveChangesAsync();

        _logger.LogInformation("[PRODUCTS] Product {Id} → {Status}", id, s);
        return Ok(new { product.ProductId, product.Status });
    }

    // ── Add review (Admin) ────────────────────────────────────────────────────

    public record ReviewRequest(int Rating, string? Notes, string Recommendation);

    [HttpPost("{id:long}/reviews")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddReview(long id, [FromBody] ReviewRequest req)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();

        var userId   = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? "Admin";

        var review = new ProductReview
        {
            ProductId       = id,
            ReviewerUserId  = userId,
            ReviewerName    = userName,
            Rating          = Math.Clamp(req.Rating, 1, 5),
            Notes           = req.Notes,
            Recommendation  = req.Recommendation,
            ReviewedAt      = DateTime.UtcNow
        };

        _db.ProductReviews.Add(review);
        await _db.SaveChangesAsync();
        return Ok(new { review.ReviewId });
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Vendor Portal — token-secured endpoints for the external supplier
//
// Auth: vendor provides their token in the X-Vendor-Token header.
// Token is stored in Azure App Settings as VENDOR_ACCESS_TOKEN.
// Admin sets VENDOR_NAME in App Settings so submitted products are tagged.
// ────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/vendor")]
[AllowAnonymous]
public class VendorController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<VendorController> _logger;

    public VendorController(BeautyDbContext db, IConfiguration config, ILogger<VendorController> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    private bool Authorized(out string vendorName)
    {
        vendorName = _config["VENDOR_NAME"] ?? "Vendor";
        var expected = _config["VENDOR_ACCESS_TOKEN"];
        if (string.IsNullOrEmpty(expected)) return false;

        Request.Headers.TryGetValue("X-Vendor-Token", out var header);
        return header.ToString() == expected;
    }

    // ── List vendor's own submissions ─────────────────────────────────────────

    [HttpGet("products")]
    public async Task<IActionResult> ListProducts()
    {
        if (!Authorized(out var vendorName))
            return Unauthorized(new { error = "Invalid vendor token" });

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.VendorName == vendorName)
            .OrderByDescending(p => p.SubmittedAt)
            .Select(p => new
            {
                p.ProductId,
                p.Name,
                p.Brand,
                p.Category,
                p.Description,
                p.Ingredients,
                p.Sku,
                p.ImageUrl,
                p.WholesalePriceCents,
                p.BilledPriceCents,
                p.PromoBilledPriceCents,
                p.Status,
                p.DeclineReason,
                p.SubmittedAt,
                p.ReviewedAt
            })
            .ToListAsync();

        return Ok(products);
    }

    // ── Submit a new product ──────────────────────────────────────────────────

    public record VendorProductRequest(
        string Name,
        string Brand,
        string Category,
        string? Description,
        string? Ingredients,
        string? Sku,
        string? ImageUrl,
        long WholesalePriceCents);

    [HttpPost("products")]
    public async Task<IActionResult> SubmitProduct([FromBody] VendorProductRequest req)
    {
        if (!Authorized(out var vendorName))
            return Unauthorized(new { error = "Invalid vendor token" });

        var wholesale = req.WholesalePriceCents;
        var product = new Product
        {
            Name                  = req.Name,
            Brand                 = req.Brand,
            Category              = req.Category,
            Description           = req.Description,
            Ingredients           = req.Ingredients,
            VendorName            = vendorName,
            Sku                   = req.Sku,
            ImageUrl              = req.ImageUrl,
            WholesalePriceCents   = wholesale,
            BilledPriceCents      = (long)(wholesale * 1.8),
            PromoBilledPriceCents = (long)(wholesale * 1.6),
            Status                = ProductStatus.Pending,
            SubmittedBy           = vendorName,
            SubmittedAt           = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[VENDOR] Product submitted: {Name} by {Vendor}", req.Name, vendorName);

        return Ok(new
        {
            product.ProductId,
            product.Status,
            message = "Product submitted for Saqqara review. You will see the status update here once reviewed."
        });
    }

    // ── Update a pending submission (vendor edits before review) ─────────────

    [HttpPut("products/{id:long}")]
    public async Task<IActionResult> UpdateProduct(long id, [FromBody] VendorProductRequest req)
    {
        if (!Authorized(out var vendorName))
            return Unauthorized(new { error = "Invalid vendor token" });

        var product = await _db.Products.FindAsync(id);
        if (product == null || product.VendorName != vendorName)
            return NotFound();

        if (product.Status != ProductStatus.Pending)
            return BadRequest(new { error = "Only pending products can be edited" });

        var wholesale = req.WholesalePriceCents;
        product.Name                  = req.Name;
        product.Brand                 = req.Brand;
        product.Category              = req.Category;
        product.Description           = req.Description;
        product.Ingredients           = req.Ingredients;
        product.Sku                   = req.Sku;
        product.ImageUrl              = req.ImageUrl;
        product.WholesalePriceCents   = wholesale;
        product.BilledPriceCents      = (long)(wholesale * 1.8);
        product.PromoBilledPriceCents = (long)(wholesale * 1.6);

        await _db.SaveChangesAsync();
        return Ok(new { product.ProductId, product.Status });
    }
}
