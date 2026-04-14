using Beauty.Api.Data;
using Beauty.Api.Models;
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
[Route("api/company-bookings")]
[Authorize]
public class CompanyBookingsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BlobStorageService _blob;
    private readonly ContractGeneratorService _contracts;

    public CompanyBookingsController(
        BeautyDbContext db,
        UserManager<ApplicationUser> userManager,
        BlobStorageService blob,
        ContractGeneratorService contracts)
    {
        _db = db;
        _userManager = userManager;
        _blob = blob;
        _contracts = contracts;
    }

    // ── POST /api/company-bookings ────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Company,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCompanyBookingRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var company = await _db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (company == null)
            return BadRequest(new { message = "No company profile found for this account." });

        var booking = new CompanyBooking
        {
            CompanyId           = company.Id,
            SubmittedByUserId   = userId,
            Title               = req.Title,
            Description         = req.Description,
            EventDate           = req.EventDate,
            EventEndDate        = req.EventEndDate,
            Location            = req.Location,
            NdaRequired         = req.NdaRequired,
            PackageLabel        = req.PackageLabel,
            PackageDiscountPercent = req.PackageDiscountPercent,
            Status              = CompanyBookingStatus.Submitted,
            CreatedAt           = DateTime.UtcNow,
            ArtistSlots         = req.ArtistSlots.Select(s => new CompanyBookingArtistSlot
            {
                ArtistId         = s.ArtistId,
                ArtistUserId     = s.ArtistUserId ?? "",
                ArtistName       = s.ArtistName,
                ServiceRequested = s.ServiceRequested,
                FeeCents         = s.FeeCents,
                Status           = SlotStatus.Pending,
            }).ToList()
        };

        _db.CompanyBookings.Add(booking);
        await _db.SaveChangesAsync();

        return Ok(new { bookingId = booking.Id, status = booking.Status.ToString() });
    }

    // ── GET /api/company-bookings ─────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMyBookings()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Admin");

        var query = _db.CompanyBookings
            .Include(b => b.Company)
            .Include(b => b.ArtistSlots)
            .AsQueryable();

        if (!isAdmin)
            query = query.Where(b => b.SubmittedByUserId == userId);

        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id, b.Title, b.EventDate, b.EventEndDate, b.Location,
                b.Status, b.NdaRequired, b.PackageLabel, b.PackageDiscountPercent,
                b.CreatedAt, b.ContractUrl, b.ContractGeneratedAt,
                Company = new { b.Company.CompanyName, b.Company.Email },
                ArtistSlots = b.ArtistSlots.Select(s => new
                {
                    s.Id, s.ArtistId, s.ArtistName, s.ServiceRequested,
                    s.FeeCents, s.Status, s.RespondedAt, s.ResponseNote
                }),
            })
            .ToListAsync();

        return Ok(bookings);
    }

    // ── GET /api/company-bookings/{id} ────────────────────────────
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Admin");

        var booking = await _db.CompanyBookings
            .Include(b => b.Company)
            .Include(b => b.ArtistSlots)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking == null) return NotFound();
        if (!isAdmin && booking.SubmittedByUserId != userId)
            return Forbid();

        return Ok(booking);
    }

    // ── GET /api/company-bookings/artist-slots ────────────────────
    // Artist sees their own pending slots
    [HttpGet("artist-slots")]
    [Authorize(Roles = "Artist,Admin")]
    public async Task<IActionResult> GetArtistSlots()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var slots = await _db.CompanyBookingArtistSlots
            .Include(s => s.CompanyBooking)
                .ThenInclude(b => b.Company)
            .Where(s => s.ArtistUserId == userId)
            .OrderByDescending(s => s.CompanyBooking.EventDate)
            .Select(s => new
            {
                s.Id, s.Status, s.ServiceRequested, s.FeeCents,
                s.RespondedAt, s.ResponseNote,
                Booking = new
                {
                    s.CompanyBooking.Id, s.CompanyBooking.Title,
                    s.CompanyBooking.EventDate, s.CompanyBooking.Location,
                    s.CompanyBooking.NdaRequired,
                    Company = s.CompanyBooking.Company.CompanyName,
                }
            })
            .ToListAsync();

        return Ok(slots);
    }

    // ── POST /api/company-bookings/slots/{slotId}/accept ─────────
    [HttpPost("slots/{slotId:long}/accept")]
    [Authorize(Roles = "Artist,Admin")]
    public async Task<IActionResult> AcceptSlot(long slotId, [FromBody] SlotResponseRequest? req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var slot = await _db.CompanyBookingArtistSlots
            .Include(s => s.CompanyBooking)
            .FirstOrDefaultAsync(s => s.Id == slotId);

        if (slot == null) return NotFound();
        if (slot.ArtistUserId != userId && !User.IsInRole("Admin")) return Forbid();
        if (slot.Status != SlotStatus.Pending)
            return BadRequest(new { message = "Slot has already been responded to." });

        slot.Status      = SlotStatus.Accepted;
        slot.RespondedAt = DateTime.UtcNow;
        slot.ResponseNote = req?.Note;

        await UpdateBookingStatus(slot.CompanyBooking);
        await _db.SaveChangesAsync();

        return Ok(new { slotId, status = "Accepted" });
    }

    // ── POST /api/company-bookings/slots/{slotId}/decline ────────
    [HttpPost("slots/{slotId:long}/decline")]
    [Authorize(Roles = "Artist,Admin")]
    public async Task<IActionResult> DeclineSlot(long slotId, [FromBody] SlotResponseRequest? req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var slot = await _db.CompanyBookingArtistSlots
            .Include(s => s.CompanyBooking)
            .FirstOrDefaultAsync(s => s.Id == slotId);

        if (slot == null) return NotFound();
        if (slot.ArtistUserId != userId && !User.IsInRole("Admin")) return Forbid();
        if (slot.Status != SlotStatus.Pending)
            return BadRequest(new { message = "Slot has already been responded to." });

        slot.Status       = SlotStatus.Declined;
        slot.RespondedAt  = DateTime.UtcNow;
        slot.ResponseNote = req?.Note;

        await UpdateBookingStatus(slot.CompanyBooking);
        await _db.SaveChangesAsync();

        return Ok(new { slotId, status = "Declined" });
    }

    // ── POST /api/company-bookings/{id}/generate-contract ────────
    [HttpPost("{id:long}/generate-contract")]
    [Authorize(Roles = "Company,Admin")]
    public async Task<IActionResult> GenerateContract(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user   = await _userManager.FindByIdAsync(userId);

        var booking = await _db.CompanyBookings
            .Include(b => b.Company)
            .Include(b => b.ArtistSlots)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking == null) return NotFound();
        if (booking.SubmittedByUserId != userId && !User.IsInRole("Admin")) return Forbid();

        var pdfBytes = _contracts.GenerateBookingContract(
            booking,
            user?.UserName ?? "Saqqara Admin"
        );

        // Upload to blob storage
        using var stream = new MemoryStream(pdfBytes);
        var url = await _blob.UploadAsync(stream, $"contract-{booking.Id}.pdf", "application/pdf");

        booking.ContractUrl         = url;
        booking.ContractGeneratedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { contractUrl = url, generatedAt = booking.ContractGeneratedAt });
    }

    // ── GET /api/company-bookings/{id}/contract ───────────────────
    // Returns a short-lived SAS URL for the contract PDF
    [HttpGet("{id:long}/contract")]
    public async Task<IActionResult> GetContract(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var booking = await _db.CompanyBookings.FindAsync(id);
        if (booking == null) return NotFound();
        if (booking.SubmittedByUserId != userId && !User.IsInRole("Admin")) return Forbid();
        if (string.IsNullOrEmpty(booking.ContractUrl))
            return NotFound(new { message = "No contract generated yet." });

        var sasUrl = _blob.GenerateSasUrl(booking.ContractUrl, expiryMinutes: 30);
        return Ok(new { url = sasUrl, expiresInMinutes = 30 });
    }

    // ── Helpers ───────────────────────────────────────────────────
    private static Task UpdateBookingStatus(CompanyBooking booking)
    {
        var slots = booking.ArtistSlots.ToList();
        if (slots.All(s => s.Status == SlotStatus.Accepted))
            booking.Status = CompanyBookingStatus.FullyAccepted;
        else if (slots.Any(s => s.Status == SlotStatus.Accepted))
            booking.Status = CompanyBookingStatus.PartiallyAccepted;
        else if (slots.All(s => s.Status == SlotStatus.Declined))
            booking.Status = CompanyBookingStatus.Rejected;

        booking.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    // ── Request DTOs ──────────────────────────────────────────────
    public record CreateCompanyBookingRequest(
        string Title,
        string? Description,
        DateTime EventDate,
        DateTime? EventEndDate,
        string Location,
        bool NdaRequired,
        string? PackageLabel,
        decimal? PackageDiscountPercent,
        int CompanyId,
        List<ArtistSlotRequest> ArtistSlots
    );

    public record ArtistSlotRequest(
        long ArtistId,
        string? ArtistUserId,
        string? ArtistName,
        string ServiceRequested,
        int? FeeCents
    );

    public record SlotResponseRequest(string? Note);
}
