using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.Company;
using Beauty.Api.Models.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/payouts")]
[Authorize(Roles = "Admin")]
public class PayoutsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PayoutsController> _logger;

    public PayoutsController(
        BeautyDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<PayoutsController> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    // ── GET /api/payouts/cycles ────────────────────────────────────────────────
    // All cycles with summary

    [HttpGet("cycles")]
    public async Task<IActionResult> GetCycles()
    {
        var cycles = await _db.PayoutCycles
            .OrderByDescending(c => c.PeriodStart)
            .Select(c => new
            {
                c.Id,
                c.PeriodStart,
                c.PeriodEnd,
                c.Status,
                c.TotalProviderAmountCents,
                c.TotalPlatformAmountCents,
                c.TotalInvoiceAmountCents,
                c.ApprovedByEmail,
                c.ApprovedAt,
                c.DisbursedAt,
                c.CreatedAt,
                ProviderCount = c.Lines.Count
            })
            .ToListAsync();

        return Ok(cycles);
    }

    // ── GET /api/payouts/cycles/{id} ───────────────────────────────────────────
    // Full cycle with all provider lines

    [HttpGet("cycles/{id:long}")]
    public async Task<IActionResult> GetCycle(long id)
    {
        var cycle = await _db.PayoutCycles
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (cycle == null) return NotFound();
        return Ok(cycle);
    }

    // ── POST /api/payouts/cycles/generate ──────────────────────────────────────
    // Calculate payout cycle from paid invoices in the given date range

    [HttpPost("cycles/generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateCycleRequest req)
    {
        // Prevent overlapping cycles
        var overlap = await _db.PayoutCycles.AnyAsync(c =>
            c.PeriodStart < req.PeriodEnd && c.PeriodEnd > req.PeriodStart &&
            c.Status != PayoutCycleStatus.Voided);

        if (overlap)
            return Conflict(new { code = "PERIOD_OVERLAP", message = "A cycle already exists covering part of this period." });

        // Find all invoices paid in the period
        var paidInvoices = await _db.Invoices
            .Include(i => i.LineItems)
            .Include(i => i.CompanyBooking)
                .ThenInclude(b => b != null ? b.ArtistSlots : null!)
            .Where(i =>
                i.Status == InvoiceStatus.Paid &&
                i.PaidAt >= req.PeriodStart &&
                i.PaidAt < req.PeriodEnd)
            .ToListAsync();

        if (!paidInvoices.Any())
            return BadRequest(new { code = "NO_PAID_INVOICES", message = "No paid invoices found in this period." });

        // Aggregate provider earnings: one line per provider per cycle
        // Provider share = sum of FeeCents for accepted slots on paid bookings
        var providerEarnings = new Dictionary<string, (string? Name, string? Email, long Cents, long? BookingId, long InvoiceId)>();

        long totalInvoiceCents  = 0;
        long totalProviderCents = 0;

        foreach (var invoice in paidInvoices)
        {
            var invoiceTotal = invoice.LineItems.Sum(l => (long)l.TotalCents);
            totalInvoiceCents += invoiceTotal;

            if (invoice.CompanyBooking == null) continue;

            foreach (var slot in invoice.CompanyBooking.ArtistSlots
                         .Where(s => s.Status == SlotStatus.Accepted && s.FeeCents.HasValue))
            {
                var userId = slot.ArtistUserId;
                if (string.IsNullOrEmpty(userId)) continue;

                if (!providerEarnings.ContainsKey(userId))
                    providerEarnings[userId] = (slot.ArtistName, null, 0, invoice.CompanyBookingId, invoice.Id);

                var current = providerEarnings[userId];
                providerEarnings[userId] = (current.Name, current.Email, current.Cents + slot.FeeCents!.Value, invoice.CompanyBookingId, invoice.Id);
                totalProviderCents += slot.FeeCents!.Value;
            }
        }

        // Enrich with emails from Identity
        foreach (var userId in providerEarnings.Keys.ToList())
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var entry = providerEarnings[userId];
                providerEarnings[userId] = (entry.Name ?? user.Email, user.Email, entry.Cents, entry.BookingId, entry.InvoiceId);
            }
        }

        var cycle = new PayoutCycle
        {
            PeriodStart                = req.PeriodStart,
            PeriodEnd                  = req.PeriodEnd,
            Status                     = PayoutCycleStatus.PendingReview,
            TotalInvoiceAmountCents    = totalInvoiceCents,
            TotalProviderAmountCents   = totalProviderCents,
            TotalPlatformAmountCents   = totalInvoiceCents - totalProviderCents,
            Notes                      = req.Notes,
            CreatedAt                  = DateTime.UtcNow,
            Lines = providerEarnings.Select(kv => new ProviderPayoutLine
            {
                ProviderUserId = kv.Key,
                ProviderName   = kv.Value.Name,
                ProviderEmail  = kv.Value.Email,
                ProviderRole   = "Artist",
                AmountCents    = kv.Value.Cents,
                Status         = PayoutLineStatus.Pending,
                BookingId      = kv.Value.BookingId,
                InvoiceId      = kv.Value.InvoiceId,
                Description    = $"Earnings for period {req.PeriodStart:MM/dd/yyyy} – {req.PeriodEnd:MM/dd/yyyy}"
            }).ToList()
        };

        _db.PayoutCycles.Add(cycle);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[PAYOUTS] Cycle {CycleId} generated: {Providers} providers, ${Total:F2} total",
            cycle.Id, cycle.Lines.Count, cycle.TotalProviderAmountCents / 100m);

        return Ok(new
        {
            cycleId              = cycle.Id,
            providerCount        = cycle.Lines.Count,
            totalProviderCents   = cycle.TotalProviderAmountCents,
            totalPlatformCents   = cycle.TotalPlatformAmountCents,
            totalInvoiceCents    = cycle.TotalInvoiceAmountCents
        });
    }

    // ── POST /api/payouts/cycles/{id}/approve ──────────────────────────────────
    // Admin reviews and approves — marks all lines Approved

    [HttpPost("cycles/{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, [FromBody] ApproveRequest req)
    {
        var cycle = await _db.PayoutCycles
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (cycle == null) return NotFound();

        if (cycle.Status != PayoutCycleStatus.PendingReview)
            return BadRequest(new { code = "INVALID_STATUS", message = $"Cycle is {cycle.Status}, not PendingReview." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email  = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email);

        cycle.Status          = PayoutCycleStatus.Approved;
        cycle.ApprovedByUserId = userId;
        cycle.ApprovedByEmail  = email;
        cycle.ApprovedAt      = DateTime.UtcNow;
        cycle.Notes           = req.Notes ?? cycle.Notes;

        // Mark non-held lines as approved
        foreach (var line in cycle.Lines.Where(l => l.Status == PayoutLineStatus.Pending))
            line.Status = PayoutLineStatus.Approved;

        // Hold any explicitly held providers
        if (req.HoldProviderIds?.Any() == true)
        {
            foreach (var line in cycle.Lines.Where(l => req.HoldProviderIds.Contains(l.ProviderUserId)))
            {
                line.Status = PayoutLineStatus.Held;
                line.Notes  = req.HoldReason ?? "Held by admin";
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("[PAYOUTS] Cycle {CycleId} approved by {Email}", id, email);
        return Ok(new { cycleId = id, status = "Approved", approvedAt = cycle.ApprovedAt });
    }

    // ── POST /api/payouts/cycles/{id}/disburse ─────────────────────────────────
    // Mark as paid — confirms physical bank transfers were executed

    [HttpPost("cycles/{id:long}/disburse")]
    public async Task<IActionResult> Disburse(long id, [FromBody] DisburseRequest req)
    {
        var cycle = await _db.PayoutCycles
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (cycle == null) return NotFound();

        if (cycle.Status != PayoutCycleStatus.Approved)
            return BadRequest(new { code = "INVALID_STATUS", message = $"Cycle must be Approved before disbursing." });

        cycle.Status      = PayoutCycleStatus.Disbursed;
        cycle.DisbursedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(req.Notes)) cycle.Notes = req.Notes;

        foreach (var line in cycle.Lines.Where(l => l.Status == PayoutLineStatus.Approved))
            line.Status = PayoutLineStatus.Disbursed;

        await _db.SaveChangesAsync();

        _logger.LogInformation("[PAYOUTS] Cycle {CycleId} disbursed — ${Total:F2} to {Count} providers",
            id, cycle.TotalProviderAmountCents / 100m, cycle.Lines.Count(l => l.Status == PayoutLineStatus.Disbursed));

        return Ok(new { cycleId = id, status = "Disbursed", disbursedAt = cycle.DisbursedAt });
    }

    // ── POST /api/payouts/cycles/{id}/void ────────────────────────────────────

    [HttpPost("cycles/{id:long}/void")]
    public async Task<IActionResult> Void(long id)
    {
        var cycle = await _db.PayoutCycles.FindAsync(id);
        if (cycle == null) return NotFound();
        if (cycle.Status == PayoutCycleStatus.Disbursed)
            return BadRequest(new { code = "ALREADY_DISBURSED" });

        cycle.Status = PayoutCycleStatus.Voided;
        await _db.SaveChangesAsync();
        return Ok(new { cycleId = id, status = "Voided" });
    }

    // ── GET /api/payouts/summary ───────────────────────────────────────────────
    // Dashboard summary stats

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var pending    = await _db.PayoutCycles.Where(c => c.Status == PayoutCycleStatus.PendingReview).CountAsync();
        var approved   = await _db.PayoutCycles.Where(c => c.Status == PayoutCycleStatus.Approved).CountAsync();
        var lastDisbursed = await _db.PayoutCycles
            .Where(c => c.Status == PayoutCycleStatus.Disbursed)
            .OrderByDescending(c => c.DisbursedAt)
            .Select(c => new { c.Id, c.DisbursedAt, c.TotalProviderAmountCents, c.TotalPlatformAmountCents })
            .FirstOrDefaultAsync();

        var pendingTotal = await _db.PayoutCycles
            .Where(c => c.Status == PayoutCycleStatus.PendingReview || c.Status == PayoutCycleStatus.Approved)
            .SumAsync(c => (long?)c.TotalProviderAmountCents) ?? 0;

        // Next suggested payout period (last 14 days)
        var lastCycle = await _db.PayoutCycles
            .Where(c => c.Status != PayoutCycleStatus.Voided)
            .OrderByDescending(c => c.PeriodEnd)
            .FirstOrDefaultAsync();

        var suggestedStart = lastCycle?.PeriodEnd ?? DateTime.UtcNow.AddDays(-14);
        var suggestedEnd   = suggestedStart.AddDays(14) > DateTime.UtcNow
            ? DateTime.UtcNow
            : suggestedStart.AddDays(14);

        return Ok(new
        {
            pendingCycles         = pending,
            approvedCycles        = approved,
            pendingAmountCents    = pendingTotal,
            lastDisbursed,
            suggestedNextPeriod   = new { start = suggestedStart, end = suggestedEnd }
        });
    }

    // ── GET /api/payouts/me ────────────────────────────────────────────────────
    // Provider's own payout history (any authenticated user)

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> MyPayouts()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var lines = await _db.ProviderPayoutLines
            .Include(l => l.Cycle)
            .Where(l => l.ProviderUserId == userId)
            .OrderByDescending(l => l.Cycle.PeriodStart)
            .Select(l => new
            {
                l.Id,
                l.AmountCents,
                l.Status,
                l.Description,
                Period = new { l.Cycle.PeriodStart, l.Cycle.PeriodEnd },
                l.Cycle.DisbursedAt
            })
            .ToListAsync();

        return Ok(lines);
    }

    // ── DTOs ───────────────────────────────────────────────────────────────────

    public record GenerateCycleRequest(
        DateTime PeriodStart,
        DateTime PeriodEnd,
        string?  Notes);

    public record ApproveRequest(
        string?         Notes,
        List<string>?   HoldProviderIds,
        string?         HoldReason);

    public record DisburseRequest(string? Notes);
}
