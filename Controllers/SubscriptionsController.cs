using Beauty.Api.Data;
using Beauty.Api.Models.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/subscriptions")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly BeautyDbContext _db;

    public SubscriptionsController(BeautyDbContext db) => _db = db;

    // ── GET /api/subscriptions ─────────────────────────────────────────
    // Admin: list all subscriptions with optional status filter

    [HttpGet]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] bool?   billingDueThisMonth)
    {
        var query = _db.ArtistSubscriptions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == Enum.Parse<SubscriptionStatus>(status, true));

        if (billingDueThisMonth == true)
        {
            var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var end   = start.AddMonths(1);
            query = query.Where(s =>
                s.Status == SubscriptionStatus.Active &&
                s.NextBillingDate >= start &&
                s.NextBillingDate < end);
        }

        var subs = await query.OrderBy(s => s.NextBillingDate).ToListAsync();
        return Ok(subs.Select(Map));
    }

    // ── GET /api/subscriptions/my ──────────────────────────────────────
    // Artist: own subscription status

    [HttpGet("my")]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var sub = await _db.ArtistSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (sub is null) return NotFound(new { error = "No subscription found for this account." });
        return Ok(Map(sub));
    }

    // ── POST /api/subscriptions/enroll/{userId} ───────────────────────
    // Admin: start a 30-day free trial for an artist on approval

    [HttpPost("enroll/{userId}")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Enroll(string userId)
    {
        if (await _db.ArtistSubscriptions.AnyAsync(s => s.UserId == userId))
            return Conflict(new { error = "Subscription already exists for this user." });

        var now = DateTime.UtcNow;
        var sub = new ArtistSubscription
        {
            UserId         = userId,
            Status         = SubscriptionStatus.Trialing,
            MonthlyAmount  = 19.00m,
            TrialStartDate = now,
            TrialEndDate   = now.AddDays(30),
            CreatedAt      = now,
            UpdatedAt      = now
        };

        _db.ArtistSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
        return Ok(Map(sub));
    }

    // ── PUT /api/subscriptions/{id}/activate ──────────────────────────
    // Admin: move subscription from Trialing → Active after first billing

    [HttpPut("{id:long}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Activate(long id, [FromBody] ActivateReq req)
    {
        var sub = await _db.ArtistSubscriptions.FindAsync(id);
        if (sub is null) return NotFound();

        var now = DateTime.UtcNow;
        sub.Status               = SubscriptionStatus.Active;
        sub.SubscriptionStartDate = now;
        sub.LastBilledDate        = now;
        sub.LastBilledAmount      = sub.MonthlyAmount;
        sub.NextBillingDate       = now.AddMonths(1);
        sub.Notes                 = req.Notes;
        sub.UpdatedAt             = now;

        await _db.SaveChangesAsync();
        return Ok(Map(sub));
    }

    public record ActivateReq(string? Notes);

    // ── PUT /api/subscriptions/{id}/status ────────────────────────────
    // Admin: suspend, cancel, or mark past-due

    [HttpPut("{id:long}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] StatusReq req)
    {
        var sub = await _db.ArtistSubscriptions.FindAsync(id);
        if (sub is null) return NotFound();

        sub.Status    = req.Status;
        sub.Notes     = req.Notes ?? sub.Notes;
        sub.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(Map(sub));
    }

    public record StatusReq(SubscriptionStatus Status, string? Notes);

    // ── PUT /api/subscriptions/{id}/record-payment ────────────────────
    // Admin: record a manual monthly payment and advance billing date

    [HttpPut("{id:long}/record-payment")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RecordPayment(long id, [FromBody] PaymentReq req)
    {
        var sub = await _db.ArtistSubscriptions.FindAsync(id);
        if (sub is null) return NotFound();

        var now = DateTime.UtcNow;
        sub.Status            = SubscriptionStatus.Active;
        sub.LastBilledDate    = now;
        sub.LastBilledAmount  = req.Amount ?? sub.MonthlyAmount;
        sub.NextBillingDate   = (sub.NextBillingDate ?? now).AddMonths(1);
        sub.Notes             = req.Notes ?? sub.Notes;
        sub.UpdatedAt         = now;

        await _db.SaveChangesAsync();
        return Ok(Map(sub));
    }

    public record PaymentReq(decimal? Amount, string? Notes);

    // ── GET /api/subscriptions/upcoming-renewals ─────────────────────
    // Power Automate polls this daily to alert Dajanay of upcoming billing
    // ?days=3 (default) — subscriptions with NextBillingDate within N days

    [HttpGet("upcoming-renewals")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> UpcomingRenewals([FromQuery] int days = 3)
    {
        if (days < 1 || days > 30) return BadRequest(new { error = "days must be 1–30" });

        var now    = DateTime.UtcNow;
        var cutoff = now.AddDays(days);

        var subs = await _db.ArtistSubscriptions
            .AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active
                     && s.NextBillingDate.HasValue
                     && s.NextBillingDate.Value >= now
                     && s.NextBillingDate.Value <= cutoff)
            .OrderBy(s => s.NextBillingDate)
            .ToListAsync();

        return Ok(new
        {
            DaysLookahead = days,
            Count         = subs.Count,
            Renewals      = subs.Select(s => new
            {
                s.Id,
                s.UserId,
                s.MonthlyAmount,
                NextBillingDate = s.NextBillingDate!.Value,
                DaysUntilDue    = (int)(s.NextBillingDate.Value - now).TotalDays
            })
        });
    }

    // ── GET /api/subscriptions/summary ────────────────────────────────
    // Admin: quick dashboard counts by status

    [HttpGet("summary")]
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Summary()
    {
        var counts = await _db.ArtistSubscriptions
            .AsNoTracking()
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var monthlyRecurring = await _db.ArtistSubscriptions
            .AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .SumAsync(s => s.MonthlyAmount);

        return Ok(new { Counts = counts, MonthlyRecurringRevenue = monthlyRecurring });
    }

    // ── Mapper ────────────────────────────────────────────────────────

    private static object Map(ArtistSubscription s) => new
    {
        s.Id,
        s.UserId,
        Status               = s.Status.ToString(),
        s.MonthlyAmount,
        s.TrialStartDate,
        s.TrialEndDate,
        s.SubscriptionStartDate,
        s.NextBillingDate,
        s.LastBilledDate,
        s.LastBilledAmount,
        s.Notes,
        TrialDaysRemaining   = s.Status == SubscriptionStatus.Trialing
                                ? Math.Max(0, (int)(s.TrialEndDate - DateTime.UtcNow).TotalDays)
                                : (int?)null,
        s.CreatedAt,
        s.UpdatedAt
    };
}
