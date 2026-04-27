using Beauty.Api.Data;
using Beauty.Api.Models.Payments;
using Beauty.Api.Models.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin,Staff")]
public class ReportsController : ControllerBase
{
    private readonly BeautyDbContext _db;

    // ── Role weights — update here when adjusted by management ────────
    // Must total 100. Reflects each role's contribution to platform growth.
    private static readonly RoleSplit[] _splits =
    [
        new("Kenny Stephen",     "CEO",                                    35m, "K.Stephen@Saqqarallc.com"),
        new("Jya Scott",         "Head of Strategic Partnerships",         13m, "J.Scott@Saqqarallc.com"),
        new("Kareem D'Oliveira", "Sales & Partnerships Coordinator",       13m, "K.D'oliveira@Saqqarallc.com"),
        new("Dajanay Fowler",    "Operations & Support",                   13m, "D.Fowler@Saqqarallc.com"),
        new("Lakwanja Bell",     "Director of Partner Ops & Compliance",   13m, "L.Bell@Saqqarallc.com"),
        new("Ahasan Stephen",    "Creative Director",                      13m, "A.Stephen@Saqqarallc.com"),
    ];

    private record RoleSplit(string Name, string Role, decimal WeightPct, string Email);

    private const decimal FixedOpsCost    = 1107.00m;
    private const decimal TeamPoolPct     = 0.40m;
    private const decimal ExpenseBudgetPct = 0.05m;  // 5% of net available → team expense pool
    private const decimal CommissionRate  = 0.15m;
    private const decimal ArtistSubFee    = 19.00m;

    public ReportsController(BeautyDbContext db) => _db = db;

    // ── GET /api/reports/monthly-compensation ─────────────────────────
    // Returns the official monthly bonus pool calculation.
    // Run on the 1st of each month for the previous month.
    // Example: GET /api/reports/monthly-compensation?year=2026&month=5

    [HttpGet("monthly-compensation")]
    public async Task<IActionResult> MonthlyCompensation(
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var y = year  ?? DateTime.UtcNow.Year;
        var m = month ?? DateTime.UtcNow.Month;

        if (m < 1 || m > 12) return BadRequest(new { error = "month must be 1–12" });

        var start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = start.AddMonths(1);

        // ── Booking commission ─────────────────────────────────────────
        var capturedPayments = await _db.WpPayments
            .AsNoTracking()
            .Where(p => p.Status == WpPaymentStatus.Captured
                     && p.CompletedAt >= start
                     && p.CompletedAt <  end)
            .Select(p => new { p.AmountCents, p.BookingId })
            .ToListAsync();

        var gmvCents        = capturedPayments.Sum(p => p.AmountCents);
        var gmvDollars      = gmvCents / 100m;
        var commissionRev   = Math.Round(gmvDollars * CommissionRate, 2);

        // ── Subscription revenue ───────────────────────────────────────
        var activeSubs      = await _db.ArtistSubscriptions
            .AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Active);
        var subscriptionRev = activeSubs * ArtistSubFee;

        // ── Pool calculation ──────────────────────────────────────────
        var totalRevenue     = commissionRev + subscriptionRev;
        var netAvailable     = totalRevenue - FixedOpsCost;
        var net              = Math.Max(0m, netAvailable);
        var teamPool         = Math.Round(net * TeamPoolPct, 2);
        var expenseBudget    = Math.Round(net * ExpenseBudgetPct, 2);
        var businessRetained = Math.Round(net - teamPool - expenseBudget, 2);

        // ── Per-person distribution ───────────────────────────────────
        var distribution = _splits.Select(s => new
        {
            s.Name,
            s.Email,
            s.Role,
            WeightPct    = s.WeightPct,
            BonusAmount  = Math.Round(teamPool * (s.WeightPct / 100m), 2)
        }).ToList();

        return Ok(new
        {
            Period              = start.ToString("MMMM yyyy"),
            BookingCount        = capturedPayments.Count,
            BookingGmv          = Math.Round(gmvDollars, 2),
            CommissionRevenue   = commissionRev,
            ActiveSubscriptions = activeSubs,
            SubscriptionRevenue = subscriptionRev,
            TotalRevenue        = Math.Round(totalRevenue, 2),
            FixedOps            = FixedOpsCost,
            NetAvailable        = Math.Round(netAvailable, 2),
            TeamPoolPercent      = (int)(TeamPoolPct * 100),
            TeamPool             = teamPool,
            ExpenseBudgetPercent = (int)(ExpenseBudgetPct * 100),
            ExpenseBudget        = expenseBudget,
            BusinessRetained     = businessRetained,
            Distribution         = distribution
        });
    }

    // ── GET /api/reports/monthly-compensation/range ───────────────────
    // Trailing N months — shows the trend for payroll conversion decisions

    [HttpGet("monthly-compensation/range")]
    public async Task<IActionResult> CompensationRange([FromQuery] int months = 3)
    {
        if (months < 1 || months > 24) return BadRequest(new { error = "months must be 1–24" });

        var results = new List<object>();
        var current = DateTime.UtcNow;

        for (int i = months - 1; i >= 0; i--)
        {
            var target = current.AddMonths(-i);
            var redirect = await MonthlyCompensation(target.Year, target.Month) as OkObjectResult;
            if (redirect?.Value is not null)
                results.Add(redirect.Value);
        }

        return Ok(results);
    }
}
