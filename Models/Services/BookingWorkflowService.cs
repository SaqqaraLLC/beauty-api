using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Services;

public class BookingWorkflowService
{
    private readonly BeautyDbContext _db;

    public BookingWorkflowService(BeautyDbContext db)
    {
        _db = db;
    }

    public async Task<Booking> CreateBookingRequestAsync(Booking booking)
    {
        booking.Status = BookingStatus.PendingApprovals;
        booking.ArtistApproval = ApprovalDecision.Pending;
        booking.LocationApproval = ApprovalDecision.Pending;

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return booking;
    }

    public async Task ApproveByArtistAsync(long bookingId, string approverUserId)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == bookingId)
            ?? throw new InvalidOperationException("Booking not found.");

        if (booking.Status == BookingStatus.Rejected || booking.Status == BookingStatus.Cancelled)
            throw new InvalidOperationException("This booking can no longer be approved.");

        booking.ArtistApproval = ApprovalDecision.Approved;
        booking.ArtistApprovedAt = DateTime.UtcNow;
        booking.ArtistApprovedByUserId = approverUserId;
        booking.RecalculateStatus();

        await _db.SaveChangesAsync();
    }

    public async Task ApproveByLocationAsync(long bookingId, string approverUserId)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == bookingId)
            ?? throw new InvalidOperationException("Booking not found.");

        if (booking.Status == BookingStatus.Rejected || booking.Status == BookingStatus.Cancelled)
            throw new InvalidOperationException("This booking can no longer be approved.");

        booking.LocationApproval = ApprovalDecision.Approved;
        booking.LocationApprovedAt = DateTime.UtcNow;
        booking.LocationApprovedByUserId = approverUserId;
        booking.RecalculateStatus();

        await _db.SaveChangesAsync();
    }

    public async Task RejectByArtistAsync(long bookingId, string reason)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == bookingId)
            ?? throw new InvalidOperationException("Booking not found.");

        booking.ArtistApproval = ApprovalDecision.Rejected;
        booking.RejectionReason = reason;
        booking.RecalculateStatus();

        await _db.SaveChangesAsync();
    }

    public async Task RejectByLocationAsync(long bookingId, string reason)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == bookingId)
            ?? throw new InvalidOperationException("Booking not found.");

        booking.LocationApproval = ApprovalDecision.Rejected;
        booking.RejectionReason = reason;
        booking.RecalculateStatus();

        await _db.SaveChangesAsync();
    }

    public async Task<bool> CanCustomerCompleteAsync(long bookingId)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == bookingId)
            ?? throw new InvalidOperationException("Booking not found.");

        return booking.CanCustomerCompleteApplication;
    }
}

