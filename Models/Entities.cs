using System;
using Microsoft.AspNetCore.Identity;

namespace Beauty.Api.Models
{
    // ASP.NET Identity entities (long keys)
    public class ApplicationUser : IdentityUser<long> { }
    public class ApplicationRole : IdentityRole<long> { }

    // Two‑party approval workflow status
    public enum BookingStatus
    {
        Pending = 0,          // created, awaiting approvals
        ArtistApproved = 1,   // artist approved; location pending
        LocationApproved = 2, // location approved; artist pending
        Approved = 3,         // both approved
        Rejected = 4,         // any party rejected
        Cancelled = 5
    }

    // Core entities
    public class Customer
    {
        public long CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    public class Artist
    {
        public long ArtistId { get; set; }
        public string FullName { get; set; } = string.Empty;
    }

    public class Location
    {
        public long LocationId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class Service
    {
        public long ServiceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
    }

    // ✅ Single, final Booking class (includes two‑party approval flags)
    public class Booking
    {
        public long BookingId { get; set; }
        public long CustomerId { get; set; }
        public long ArtistId { get; set; }
        public long ServiceId { get; set; }
        public long LocationId { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }

        public bool ArtistApproved { get; set; } = false;
        public bool LocationApproved { get; set; } = false;
        public DateTime? ArtistApprovedAt { get; set; }
        public DateTime? LocationApprovedAt { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Pending;
    }

    public class StreamQuotaOverride
    {
        public long OverrideId { get; set; }
        public long ArtistId { get; set; }
        public long RequestedByOwnerId { get; set; }
        public string ApprovalStatus { get; set; } = "pending";
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public string? Reason { get; set; }
    }

 
        public class Employee
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = default!;
            public string LastName { get; set; } = default!;
            public string? Email { get; set; }
            public DateTime? HireDate { get; set; }
            public bool Active { get; set; } = true;
        }
    