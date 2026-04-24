using Beauty.Api.Data;
using Beauty.Api.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

// ── Vendor Portal — token-secured endpoints for external suppliers ─────────────
//
// Auth: vendor provides their secret token in the X-Vendor-Token header.
// VENDOR_ACCESS_TOKEN and VENDOR_NAME are configured in Azure App Settings.
// Products land as Pending and are invisible to artists until Admin approves them.
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/vendor")]
[AllowAnonymous]
public class VendorController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<VendorController> _logger;

    public VendorController(
        BeautyDbContext db,
        IConfiguration config,
        ILogger<VendorController> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    private bool IsAuthorized(out string vendorName)
    {
        vendorName = _config["VENDOR_NAME"] ?? "Vendor";
        var expected = _config["VENDOR_ACCESS_TOKEN"];
        if (string.IsNullOrEmpty(expected)) return false;

        Request.Headers.TryGetValue("X-Vendor-Token", out var header);
        return header.ToString() == expected;
    }

    // ── GET /api/vendor/products — list this vendor's submissions ─────────────

    [HttpGet("products")]
    public async Task<IActionResult> ListProducts()
    {
        if (!IsAuthorized(out var vendorName))
            return Unauthorized(new { error = "Invalid vendor token" });

        var products = (await _db.Products
            .AsNoTracking()
            .Where(p => p.VendorName == vendorName)
            .OrderByDescending(p => p.SubmittedAt)
            .ToListAsync())
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
                Status        = p.Status.ToString(),
                p.IsActive,
                p.DeclineReason,
                p.SubmittedAt,
                p.ApprovedAt,
                p.DeclinedAt
            });

        return Ok(products);
    }

    // ── POST /api/vendor/products — submit a new product ─────────────────────

    public record VendorProductRequest(
        string  Name,
        string  Brand,
        string? Category,
        string? Description,
        string? Ingredients,
        string? Sku,
        string? ImageUrl,
        int     WholesalePriceCents);

    [HttpPost("products")]
    public async Task<IActionResult> SubmitProduct([FromBody] VendorProductRequest req)
    {
        if (!IsAuthorized(out var vendorName))
            return Unauthorized(new { error = "Invalid vendor token" });

        var product = new Product
        {
            Name                = req.Name,
            Brand               = req.Brand,
            Category            = req.Category ?? "Other",
            Description         = req.Description,
            Ingredients         = req.Ingredients,
            Sku                 = req.Sku,
            ImageUrl            = req.ImageUrl,
            VendorName          = vendorName,
            WholesalePriceCents = req.WholesalePriceCents,
            Status              = ProductStatus.Pending,
            IsActive            = false,
            SubmittedAt         = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[VENDOR] Submitted: {Name} by {Vendor}", product.Name, vendorName);

        return Ok(new
        {
            product.ProductId,
            Status  = product.Status.ToString(),
            message = "Product submitted for Saqqara review. Status will update once reviewed."
        });
    }

    // ── PUT /api/vendor/products/{id} — edit a pending submission ────────────

    [HttpPut("products/{id:int}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] VendorProductRequest req)
    {
        if (!IsAuthorized(out var vendorName))
            return Unauthorized(new { error = "Invalid vendor token" });

        var product = await _db.Products.FindAsync(id);
        if (product == null || product.VendorName != vendorName)
            return NotFound();

        if (product.Status != ProductStatus.Pending)
            return BadRequest(new { error = "Only pending products can be edited" });

        product.Name                = req.Name;
        product.Brand               = req.Brand;
        product.Category            = req.Category ?? product.Category;
        product.Description         = req.Description;
        product.Ingredients         = req.Ingredients;
        product.Sku                 = req.Sku;
        product.ImageUrl            = req.ImageUrl;
        product.WholesalePriceCents = req.WholesalePriceCents;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            product.ProductId,
            Status = product.Status.ToString()
        });
    }
}
