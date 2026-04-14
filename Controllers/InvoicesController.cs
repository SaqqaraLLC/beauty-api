using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Catalog;
using Beauty.Api.Models.Company;
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
[Route("api/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BlobStorageService _blob;
    private readonly InvoiceGeneratorService _generator;

    public InvoicesController(
        BeautyDbContext db,
        UserManager<ApplicationUser> userManager,
        BlobStorageService blob,
        InvoiceGeneratorService generator)
    {
        _db = db;
        _userManager = userManager;
        _blob = blob;
        _generator = generator;
    }

    // ── POST /api/invoices ────────────────────────────────────────
    // Admin or Company creates an invoice (optionally linked to a booking)
    [HttpPost]
    [Authorize(Roles = "Admin,Company")]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var company = await _db.CompanyProfiles.FindAsync(req.CompanyId);
        if (company == null)
            return BadRequest(new { message = "Company not found." });

        var number = await GenerateInvoiceNumber();

        var invoice = new Invoice
        {
            InvoiceNumber      = number,
            CompanyId          = req.CompanyId,
            CompanyBookingId   = req.CompanyBookingId,
            IssuedByUserId     = userId,
            IssuedAt           = DateTime.UtcNow,
            DueAt              = req.DueAt ?? DateTime.UtcNow.AddDays(14),
            Notes              = req.Notes,
            Status             = InvoiceStatus.Draft,
            LineItems          = req.LineItems.Select(l => new InvoiceLineItem
            {
                Description      = l.Description,
                Quantity         = l.Quantity,
                UnitPriceCents   = l.UnitPriceCents,
                DiscountPercent  = l.DiscountPercent,
            }).ToList()
        };

        // Auto-populate line items from booking artist slots if linked
        if (req.CompanyBookingId.HasValue && !req.LineItems.Any())
        {
            var booking = await _db.CompanyBookings
                .Include(b => b.ArtistSlots)
                .FirstOrDefaultAsync(b => b.Id == req.CompanyBookingId);

            if (booking != null)
            {
                invoice.LineItems = booking.ArtistSlots
                    .Where(s => s.Status == SlotStatus.Accepted && s.FeeCents.HasValue)
                    .Select(s => new InvoiceLineItem
                    {
                        Description    = $"{s.ArtistName ?? $"Artist #{s.ArtistId}"} — {s.ServiceRequested}",
                        Quantity       = 1,
                        UnitPriceCents = s.FeeCents!.Value,
                    }).ToList();
            }
        }

        // Resolve promo code (if provided)
        Beauty.Api.Models.Catalog.PromoCode? promo = null;
        if (!string.IsNullOrWhiteSpace(req.PromoCode))
        {
            promo = await _db.PromoCodes
                .FirstOrDefaultAsync(p =>
                    p.Code == req.PromoCode.Trim().ToUpper() && p.IsActive);

            if (promo != null && promo.IsValid)
            {
                promo.UsedCount++;    // increment usage counter
            }
            else
            {
                return BadRequest(new { message = "Promo code is invalid or expired." });
            }
        }

        // Auto-add %PURE product kit line items
        if (req.ProductIds != null && req.ProductIds.Any())
        {
            var products = await _db.Products
                .Where(p => req.ProductIds.Contains(p.ProductId) &&
                            p.Status == ProductStatus.Approved &&
                            p.IsActive)
                .ToListAsync();

            // Use promo multiplier if code applied, otherwise standard 80% markup
            var multiplier = promo?.ProductMarkupMultiplier ?? 1.8m;
            var promoLabel = promo != null ? " (Promo)" : "";

            foreach (var product in products)
            {
                var unitPrice = (int)(product.WholesalePriceCents * multiplier);
                invoice.LineItems.Add(new InvoiceLineItem
                {
                    Description    = $"%PURE {product.Name} ({product.Brand}) — Personal Kit{promoLabel}",
                    Quantity       = 1,
                    UnitPriceCents = unitPrice,
                });
            }
        }

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        return Ok(new { invoiceId = invoice.Id, invoiceNumber = invoice.InvoiceNumber });
    }

    // ── GET /api/invoices ─────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Admin");

        var query = _db.Invoices
            .Include(i => i.Company)
            .Include(i => i.LineItems)
            .AsQueryable();

        if (!isAdmin)
        {
            // Company users see their own invoices
            var company = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.UserId == userId);
            if (company == null) return Ok(Array.Empty<object>());
            query = query.Where(i => i.CompanyId == company.Id);
        }

        var invoices = await query
            .OrderByDescending(i => i.IssuedAt)
            .Select(i => new
            {
                i.Id, i.InvoiceNumber, i.Status, i.IssuedAt, i.DueAt, i.PaidAt,
                i.PdfUrl, i.CompanyBookingId,
                Company   = new { i.Company.CompanyName, i.Company.Email },
                TotalCents = i.LineItems.Sum(l => l.Quantity * l.UnitPriceCents),
            })
            .ToListAsync();

        return Ok(invoices);
    }

    // ── GET /api/invoices/{id} ────────────────────────────────────
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company)
            .Include(i => i.LineItems)
            .Include(i => i.CompanyBooking)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound();
        if (!CanAccess(invoice)) return Forbid();

        return Ok(invoice);
    }

    // ── POST /api/invoices/{id}/generate-pdf ─────────────────────
    [HttpPost("{id:long}/generate-pdf")]
    [Authorize(Roles = "Admin,Company")]
    public async Task<IActionResult> GeneratePdf(long id)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company)
            .Include(i => i.LineItems)
            .Include(i => i.CompanyBooking)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound();
        if (!CanAccess(invoice)) return Forbid();

        var pdfBytes = _generator.GenerateInvoice(invoice);

        using var stream = new MemoryStream(pdfBytes);
        var blobName = await _blob.UploadAsync(stream, $"invoice-{invoice.InvoiceNumber}.pdf", "application/pdf");

        invoice.PdfUrl          = blobName;
        invoice.PdfGeneratedAt  = DateTime.UtcNow;

        // Move to Sent if still Draft
        if (invoice.Status == InvoiceStatus.Draft)
            invoice.Status = InvoiceStatus.Sent;

        await _db.SaveChangesAsync();

        var sasUrl = _blob.GenerateSasUrl(blobName, expiryMinutes: 60);
        return Ok(new { url = sasUrl, invoiceNumber = invoice.InvoiceNumber });
    }

    // ── GET /api/invoices/{id}/pdf ────────────────────────────────
    [HttpGet("{id:long}/pdf")]
    public async Task<IActionResult> GetPdf(long id)
    {
        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null) return NotFound();
        if (!CanAccess(invoice)) return Forbid();
        if (string.IsNullOrEmpty(invoice.PdfUrl))
            return NotFound(new { message = "PDF not generated yet." });

        var sasUrl = _blob.GenerateSasUrl(invoice.PdfUrl, expiryMinutes: 30);
        return Ok(new { url = sasUrl });
    }

    // ── POST /api/invoices/{id}/mark-paid ────────────────────────
    [HttpPost("{id:long}/mark-paid")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MarkPaid(long id)
    {
        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null) return NotFound();

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { invoiceId = id, status = "Paid" });
    }

    // ── POST /api/invoices/{id}/void ─────────────────────────────
    [HttpPost("{id:long}/void")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Void(long id)
    {
        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null) return NotFound();

        invoice.Status = InvoiceStatus.Void;
        await _db.SaveChangesAsync();

        return Ok(new { invoiceId = id, status = "Void" });
    }

    // ── Helpers ───────────────────────────────────────────────────
    private bool CanAccess(Invoice invoice)
    {
        if (User.IsInRole("Admin")) return true;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return invoice.IssuedByUserId == userId;
    }

    private async Task<string> GenerateInvoiceNumber()
    {
        var count = await _db.Invoices.CountAsync();
        return $"INV-{(count + 1):D6}";
    }

    // ── DTOs ──────────────────────────────────────────────────────
    public record CreateInvoiceRequest(
        long CompanyId,
        long? CompanyBookingId,
        DateTime? DueAt,
        string? Notes,
        List<LineItemRequest> LineItems,
        List<int>? ProductIds,         // optional %PURE product kit items
        string? PromoCode              // optional promo code (e.g. lowers product markup 80%→60%)
    );

    public record LineItemRequest(
        string Description,
        int Quantity,
        int UnitPriceCents,
        decimal? DiscountPercent
    );
}
