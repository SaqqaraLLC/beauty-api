using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Data;

public class BeautyDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public BeautyDbContext(DbContextOptions<BeautyDbContext> options)
        : base(options)
    {
    }
    public DbSet<ApprovalHistory> ApprovalHistories => Set<ApprovalHistory>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingApprovalHistory> BookingApprovalHistories { get; set; } = null!;
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<StreamQuotaOverride> StreamQuotaOverrides => Set<StreamQuotaOverride>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Booking>(entity =>
        {
            entity.HasKey(x => x.BookingId);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(x => x.ArtistApproval)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(x => x.LocationApproval)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(x => x.ArtistApprovedByUserId)
                .HasMaxLength(255);

            entity.Property(x => x.LocationApprovedByUserId)
                .HasMaxLength(255);

            entity.Property(x => x.RejectionReason)
                .HasMaxLength(1000);

        });
       
        builder.Entity<BookingApprovalHistory>()
            .HasIndex(x => new { x.BookingId, x.Stage })
            .IsUnique();

        builder.Entity<StreamQuotaOverride>(entity =>
        {
            entity.HasKey(x => x.OverrideId);

            entity.Property(x => x.ApprovalStatus)
                .HasMaxLength(50);

            entity.Property(x => x.Reason)
                .HasMaxLength(1000);
        });
    }

    

}

