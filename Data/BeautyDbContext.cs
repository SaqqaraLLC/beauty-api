using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Beauty.Api.Models.Enterprise;
using Beauty.Api.Models.Locations;
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

    // ── Existing sets ──────────────────────────────────────────────
    public DbSet<ApprovalHistory> ApprovalHistories => Set<ApprovalHistory>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingApprovalHistory> BookingApprovalHistories { get; set; } = null!;
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<StreamQuotaOverride> StreamQuotaOverrides => Set<StreamQuotaOverride>();

    // ── Enterprise — canonical tenant entities ─────────────────────
    public DbSet<EnterpriseAccount>  EnterpriseAccounts  => Set<EnterpriseAccount>();
    public DbSet<EnterpriseUser>     EnterpriseUsers     => Set<EnterpriseUser>();
    public DbSet<EnterpriseRole>     EnterpriseRoles     => Set<EnterpriseRole>();
    public DbSet<Permission>         Permissions         => Set<Permission>();
    public DbSet<RolePermission>     RolePermissions     => Set<RolePermission>();
    public DbSet<EnterpriseClient>   EnterpriseClients   => Set<EnterpriseClient>();
    public DbSet<Payment>            Payments            => Set<Payment>();
    public DbSet<AuditLog>           AuditLogs           => Set<AuditLog>();

    // ── Platform profile sets ──────────────────────────────────────
    public DbSet<ArtistProfile> ArtistProfiles => Set<ArtistProfile>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<AgentRosterEntry> AgentRosterEntries => Set<AgentRosterEntry>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FeaturedSlot> FeaturedSlots => Set<FeaturedSlot>();
    public DbSet<AvailabilityBlock> AvailabilityBlocks => Set<AvailabilityBlock>();
    public DbSet<Models.Enterprise.Stream> Streams => Set<Models.Enterprise.Stream>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Existing configuration ─────────────────────────────────
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

        // ── ArtistProfile ─────────────────────────────────────────
        builder.Entity<ArtistProfile>(entity =>
        {
            entity.HasKey(x => x.ArtistProfileId);

            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Specialty).HasMaxLength(100);
            entity.Property(x => x.Bio).HasMaxLength(2000);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.State).HasMaxLength(100);
            entity.Property(x => x.Country).HasMaxLength(100);
            entity.Property(x => x.ProfileImageUrl).HasMaxLength(500);
            entity.Property(x => x.AgencyName).HasMaxLength(200);
            entity.Property(x => x.WebsiteUrl).HasMaxLength(500);
            entity.Property(x => x.SpecialtiesJson).HasColumnType("longtext").IsRequired();
            entity.Property(x => x.HourlyRate).HasColumnType("decimal(10,2)");
            entity.Property(x => x.AverageRating).HasColumnType("double");

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.IsVerified);
        });

        // ── AgentProfile ──────────────────────────────────────────
        builder.Entity<AgentProfile>(entity =>
        {
            entity.HasKey(x => x.AgentProfileId);

            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AgencyName).HasMaxLength(200);
            entity.Property(x => x.Bio).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SpecialtiesJson).HasColumnType("longtext").IsRequired();
            entity.Property(x => x.WebsiteUrl).HasMaxLength(500);
            entity.Property(x => x.AverageRating).HasColumnType("double");

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.Status);
        });

        // ── AgentRosterEntry ──────────────────────────────────────
        builder.Entity<AgentRosterEntry>(entity =>
        {
            entity.HasKey(x => x.RosterId);

            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(x => x.AgentProfile)
                .WithMany()
                .HasForeignKey(x => x.AgentProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ArtistProfile)
                .WithMany()
                .HasForeignKey(x => x.ArtistProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.AgentProfileId);
            entity.HasIndex(x => x.ArtistProfileId);
            entity.HasIndex(x => new { x.AgentProfileId, x.ArtistProfileId }).IsUnique();
        });

        // ── Review ────────────────────────────────────────────────
        builder.Entity<Review>(entity =>
        {
            entity.HasKey(x => x.ReviewId);

            entity.Property(x => x.ReviewerUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ReviewerRole).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ReviewerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ReviewerAvatarUrl).HasMaxLength(500);
            entity.Property(x => x.SubjectEntityType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SubjectName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Body).HasMaxLength(3000);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();

            entity.HasIndex(x => new { x.SubjectEntityType, x.SubjectEntityId });
            entity.HasIndex(x => x.ReviewerUserId);
            entity.HasIndex(x => x.Status);
        });

        // ── Notification ──────────────────────────────────────────
        builder.Entity<Notification>(entity =>
        {
            entity.HasKey(x => x.NotificationId);

            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(100);
            entity.Property(x => x.ActionUrl).HasMaxLength(500);

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => new { x.UserId, x.IsRead });
        });

        // ── FeaturedSlot ──────────────────────────────────────────
        builder.Entity<FeaturedSlot>(entity =>
        {
            entity.HasKey(x => x.SlotId);

            entity.Property(x => x.SlotType).HasMaxLength(50).IsRequired();

            entity.HasOne(x => x.ArtistProfile)
                .WithMany()
                .HasForeignKey(x => x.ArtistProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.DisplayPosition);
            entity.HasIndex(x => x.ArtistProfileId);
        });

        // ── AvailabilityBlock ─────────────────────────────────────
        builder.Entity<AvailabilityBlock>(entity =>
        {
            entity.HasKey(x => x.BlockId);

            entity.Property(x => x.Note).HasMaxLength(500);

            entity.HasOne(x => x.ArtistProfile)
                .WithMany()
                .HasForeignKey(x => x.ArtistProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ArtistProfileId, x.Date });
        });

        // ── Stream ────────────────────────────────────────────────
        builder.Entity<Models.Enterprise.Stream>(entity =>
        {
            entity.HasKey(x => x.StreamId);

            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ThumbnailUrl).HasMaxLength(500);
            entity.Property(x => x.TagsJson).HasColumnType("longtext").IsRequired();

            entity.HasOne(x => x.ArtistProfile)
                .WithMany()
                .HasForeignKey(x => x.ArtistProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.IsLive);
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.ArtistProfileId);
        });

        // ── EnterpriseAccount ─────────────────────────────────────
        builder.Entity<EnterpriseAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LegalName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.BillingTier).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Status);
        });

        // ── EnterpriseRole ────────────────────────────────────────
        builder.Entity<EnterpriseRole>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.Name, x.Scope }).IsUnique();
        });

        // ── Permission ────────────────────────────────────────────
        builder.Entity<Permission>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        // ── RolePermission (junction) ─────────────────────────────
        builder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(x => new { x.RoleId, x.PermissionId });

            entity.HasOne(x => x.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── EnterpriseUser ────────────────────────────────────────
        builder.Entity<EnterpriseUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(x => x.EnterpriseAccount)
                .WithMany()
                .HasForeignKey(x => x.EnterpriseAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => new { x.EnterpriseAccountId, x.Email }).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        // ── EnterpriseClient ──────────────────────────────────────
        builder.Entity<EnterpriseClient>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(x => x.EnterpriseAccount)
                .WithMany()
                .HasForeignKey(x => x.EnterpriseAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => new { x.EnterpriseAccountId, x.Email }).IsUnique();
        });

        // ── Payment ───────────────────────────────────────────────
        builder.Entity<Payment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ProcessorReference).HasMaxLength(200);

            entity.HasOne(x => x.EnterpriseAccount)
                .WithMany()
                .HasForeignKey(x => x.EnterpriseAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Booking)
                .WithMany(b => b.Payments)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => x.BookingId);
            entity.HasIndex(x => x.Status);
        });

        // ── AuditLog ──────────────────────────────────────────────
        builder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(200);

            entity.HasOne(x => x.EnterpriseAccount)
                .WithMany()
                .HasForeignKey(x => x.EnterpriseAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => x.ActorUserId);
            entity.HasIndex(x => x.Timestamp);
            entity.HasIndex(x => new { x.TargetType, x.TargetId });
        });

        // ── Location (update: EnterpriseAccountId FK) ─────────────
        builder.Entity<Models.Locations.Location>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Timezone).HasMaxLength(100);

            entity.HasOne(x => x.EnterpriseAccount)
                .WithMany()
                .HasForeignKey(x => x.EnterpriseAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => x.IsActive);
        });
    }
}
