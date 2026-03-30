
using Beauty.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Data;

public class BeautyDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, long>
{
    public BeautyDbContext(DbContextOptions<BeautyDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<StreamQuotaOverride> StreamQuotaOverrides => Set<StreamQuotaOverride>();
    public DbSet<Employee> Employees { get; set; } = default!;
    // optional: modelBuilder.Entity<Employee>().ToTable("Employees");

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Customer>().ToTable("customers").HasKey(x => x.CustomerId);
        b.Entity<Artist>().ToTable("artists").HasKey(x => x.ArtistId);
        b.Entity<Location>().ToTable("locations").HasKey(x => x.LocationId);
        b.Entity<Service>().ToTable("services").HasKey(x => x.ServiceId);
        b.Entity<Booking>().ToTable("bookings").HasKey(x => x.BookingId);
        b.Entity<StreamQuotaOverride>().ToTable("stream_quota_overrides").HasKey(x => x.OverrideId);
        b.Entity<Booking>().Property(x => x.Status).HasConversion<string>();
    }
}
