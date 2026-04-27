using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Beauty.Api.Models.Catalog;
using Beauty.Api.Models.Company;
using Beauty.Api.Models.Enterprise;
using Beauty.Api.Models.Locations;
using Beauty.Api.Models.Payments;
using Beauty.Api.Models.Payouts;
using Beauty.Api.Models.Expenses;
using Beauty.Api.Models.Moderation;
using Beauty.Api.Models.Services;
using Beauty.Api.Models.Subscriptions;
using Beauty.Api.Models.Gifts;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Data;

public class BeautyDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    private readonly ITenantContext _tenant;

    public BeautyDbContext(
        DbContextOptions<BeautyDbContext> options,
        ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    // ── Existing sets ──────────────────────────────────────────────
    public DbSet<ApprovalHistory> ApprovalHistories => Set<ApprovalHistory>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingApprovalHistory> BookingApprovalHistories { get; set; } = null!;
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Service>         Services         => Set<Service>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ServiceAddOn>    ServiceAddOns     => Set<ServiceAddOn>();
    public DbSet<StreamQuotaOverride> StreamQuotaOverrides => Set<StreamQuotaOverride>();

    // ── Enterprise — canonical tenant entities ─────────────────────
    public DbSet<EnterpriseAccount>         EnterpriseAccounts         => Set<EnterpriseAccount>();
    public DbSet<EnterpriseContractHistory> EnterpriseContractHistories => Set<EnterpriseContractHistory>();
    public DbSet<EnterpriseUser>            EnterpriseUsers             => Set<EnterpriseUser>();
    public DbSet<EnterpriseRole>     EnterpriseRoles     => Set<EnterpriseRole>();
    public DbSet<Permission>         Permissions         => Set<Permission>();
    public DbSet<RolePermission>     RolePermissions     => Set<RolePermission>();
    public DbSet<EnterpriseClient>   EnterpriseClients   => Set<EnterpriseClient>();
    public DbSet<Payment>            Payments            => Set<Payment>();
    public DbSet<AuditLog>           AuditLogs           => Set<AuditLog>();

    // ── Company Bookings ──────────────────────────────────────────
    public DbSet<CompanyProfile>            CompanyProfiles            => Set<CompanyProfile>();
    public DbSet<CompanyBooking>            CompanyBookings            => Set<CompanyBooking>();
    public DbSet<CompanyBookingArtistSlot>  CompanyBookingArtistSlots  => Set<CompanyBookingArtistSlot>();
    public DbSet<Invoice>                   Invoices                   => Set<Invoice>();
    public DbSet<InvoiceLineItem>           InvoiceLineItems           => Set<InvoiceLineItem>();

    // ── Product Catalog ───────────────────────────────────────────
    public DbSet<Product>       Products       => Set<Product>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<PromoCode>     PromoCodes     => Set<PromoCode>();

    // ── Worldpay payments ─────────────────────────────────────────
    public DbSet<WpPayment>         WpPayments         => Set<WpPayment>();
    public DbSet<WpPaymentRefund>   WpPaymentRefunds   => Set<WpPaymentRefund>();
    public DbSet<WpPaymentAuditLog> WpPaymentAuditLogs => Set<WpPaymentAuditLog>();

    // ── Payouts ───────────────────────────────────────────────────
    public DbSet<PayoutCycle>       PayoutCycles       => Set<PayoutCycle>();
    public DbSet<ProviderPayoutLine> ProviderPayoutLines => Set<ProviderPayoutLine>();

    // ── Moderation ────────────────────────────────────────────────
    public DbSet<StreamFlag>              StreamFlags              => Set<StreamFlag>();
    public DbSet<ServiceRequiredProduct>  ServiceRequiredProducts  => Set<ServiceRequiredProduct>();

    // ── Artist Subscriptions ───────────────────────────────────────
    public DbSet<ArtistSubscription> ArtistSubscriptions => Set<ArtistSubscription>();

    // ── Expenses ──────────────────────────────────────────────────
    public DbSet<Expense> Expenses => Set<Expense>();

    // ── Gifting system ────────────────────────────────────────────
    public DbSet<GiftCatalogItem>  GiftCatalog       => Set<GiftCatalogItem>();
    public DbSet<UserWallet>       UserWallets        => Set<UserWallet>();
    public DbSet<GiftTransaction>  GiftTransactions   => Set<GiftTransaction>();
    public DbSet<SlabPurchase>     SlabPurchases      => Set<SlabPurchase>();
    public DbSet<ArtistBattle>     ArtistBattles      => Set<ArtistBattle>();
    public DbSet<BattleSignup>     BattleSignups      => Set<BattleSignup>();

    // ── Documents ─────────────────────────────────────────────────
    public DbSet<UserDocument> UserDocuments => Set<UserDocument>();

    // ── Platform profile sets ──────────────────────────────────────
    public DbSet<ArtistProfile> ArtistProfiles => Set<ArtistProfile>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<AgentRosterEntry> AgentRosterEntries => Set<AgentRosterEntry>();
    public DbSet<RepresentationRequest> RepresentationRequests => Set<RepresentationRequest>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FeaturedSlot> FeaturedSlots => Set<FeaturedSlot>();
    public DbSet<AvailabilityBlock> AvailabilityBlocks => Set<AvailabilityBlock>();
    public DbSet<Models.Enterprise.Stream> Streams => Set<Models.Enterprise.Stream>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ══════════════════════════════════════════════════════════
        // GLOBAL QUERY FILTERS
        // All tenant-scoped entities are filtered by:
        //   1. Tenant isolation  — EnterpriseAccountId == CurrentTenantId
        //                         (bypassed for platform users)
        //   2. Soft delete       — !IsDeleted
        // ══════════════════════════════════════════════════════════

        builder.Entity<EnterpriseAccount>()
            .HasQueryFilter(e => !e.IsDeleted);

        builder.Entity<Models.Locations.Location>()
            .HasQueryFilter(e =>
                !e.IsDeleted &&
                (_tenant.IsPlatformUser || e.EnterpriseAccountId == _tenant.CurrentTenantId));

        builder.Entity<EnterpriseUser>()
            .HasQueryFilter(e =>
                !e.IsDeleted &&
                (_tenant.IsPlatformUser || e.EnterpriseAccountId == _tenant.CurrentTenantId));

        builder.Entity<EnterpriseClient>()
            .HasQueryFilter(e =>
                !e.IsDeleted &&
                (_tenant.IsPlatformUser || e.EnterpriseAccountId == _tenant.CurrentTenantId));

        builder.Entity<AuditLog>()
            .HasQueryFilter(e =>
                _tenant.IsPlatformUser || e.EnterpriseAccountId == _tenant.CurrentTenantId);

        builder.Entity<Payment>()
            .HasQueryFilter(e =>
                _tenant.IsPlatformUser || e.EnterpriseAccountId == _tenant.CurrentTenantId);

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

        // ── RepresentationRequest ─────────────────────────────────
        builder.Entity<RepresentationRequest>(entity =>
        {
            entity.HasKey(x => x.RepresentationRequestId);

            entity.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000);
            entity.Property(x => x.ResponseNote).HasMaxLength(500);
            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

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
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => new { x.AgentProfileId, x.ArtistProfileId });
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

        // ── UserDocument ─────────────────────────────────────────
        builder.Entity<UserDocument>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.OwnerType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DocumentName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.DocumentNumber).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);
            entity.Property(x => x.FileUrl).HasMaxLength(1000);
            entity.Property(x => x.ReviewedByUserId).HasMaxLength(450);

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.OwnerType);
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
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PlanTier).HasMaxLength(100).IsRequired();
            entity.Property(x => x.BillingCycle).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ContractAmount).HasColumnType("decimal(12,2)");
            entity.HasMany(x => x.ContractHistory)
                  .WithOne(h => h.EnterpriseAccount)
                  .HasForeignKey(h => h.EnterpriseAccountId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.ContractRenewalDate);
        });

        // ── EnterpriseContractHistory ─────────────────────────────
        builder.Entity<EnterpriseContractHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ChangeType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PreviousTier).HasMaxLength(100);
            entity.Property(x => x.NewTier).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PreviousBillingCycle).HasMaxLength(20);
            entity.Property(x => x.NewBillingCycle).HasMaxLength(20).IsRequired();
            entity.Property(x => x.PreviousContractAmount).HasColumnType("decimal(12,2)");
            entity.Property(x => x.NewContractAmount).HasColumnType("decimal(12,2)");
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.ChangedByUserId).HasMaxLength(450);
            entity.Property(x => x.ChangedByName).HasMaxLength(200);
            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => x.EffectiveDate);
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
            entity.Property(x => x.ActorEmail).HasMaxLength(256);
            entity.Property(x => x.Action).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TargetEntity).HasMaxLength(300);
            entity.Property(x => x.Details).HasMaxLength(2000);
            entity.Property(x => x.IpAddress).HasMaxLength(64);

            // EnterpriseAccountId is optional — system events have no tenant
            entity.HasOne(x => x.EnterpriseAccount)
                .WithMany()
                .HasForeignKey(x => x.EnterpriseAccountId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => x.ActorUserId);
            entity.HasIndex(x => x.Timestamp);
            entity.HasIndex(x => x.Action);
        });

        // ── Location ──────────────────────────────────────────────
        builder.Entity<Models.Locations.Location>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(500).IsRequired();
            entity.Property(x => x.OwnerUserId).HasMaxLength(450);
            entity.Property(x => x.PureAccountStatus).HasMaxLength(30);

            entity.Ignore(x => x.PureFirstOrderDaysRemaining);

            entity.HasOne(x => x.EnterpriseAccount)
                .WithMany()
                .HasForeignKey(x => x.EnterpriseAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.EnterpriseAccountId);
            entity.HasIndex(x => x.OwnerUserId);
            entity.HasIndex(x => x.PureAccountStatus);
        });

        // ── CompanyBooking ─────────────────────────────────────────
        builder.Entity<CompanyBooking>(entity =>
        {
            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(x => x.PackageDiscountPercent)
                .HasColumnType("decimal(5,2)");

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.ArtistSlots)
                .WithOne(x => x.CompanyBooking)
                .HasForeignKey(x => x.CompanyBookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CompanyBookingArtistSlot>(entity =>
        {
            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // ── Invoice ────────────────────────────────────────────────
        builder.Entity<Invoice>(entity =>
        {
            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CompanyBooking)
                .WithMany()
                .HasForeignKey(x => x.CompanyBookingId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(x => x.LineItems)
                .WithOne(x => x.Invoice)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InvoiceLineItem>(entity =>
        {
            entity.Property(x => x.DiscountPercent)
                .HasColumnType("decimal(5,2)");

            entity.Ignore(x => x.TotalCents);
        });

        // ── Product ────────────────────────────────────────────────
        builder.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.ProductId);

            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Brand).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Ingredients).HasMaxLength(3000);
            entity.Property(x => x.Sku).HasMaxLength(100);
            entity.Property(x => x.VendorName).HasMaxLength(200);
            entity.Property(x => x.ImageUrl).HasMaxLength(500);
            entity.Property(x => x.DeclineReason).HasMaxLength(1000);
            entity.Property(x => x.SubmittedByUserId).HasMaxLength(450);
            entity.Property(x => x.AverageRating).HasColumnType("double");

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Ignore(x => x.BilledPriceCents);

            entity.HasMany(x => x.Reviews)
                .WithOne(x => x.Product)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => x.VendorName);
            entity.HasIndex(x => x.IsActive);
        });

        // ── PromoCode ──────────────────────────────────────────────
        builder.Entity<PromoCode>(entity =>
        {
            entity.HasKey(x => x.PromoCodeId);

            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(300);
            entity.Property(x => x.ProductMarkupMultiplier).HasColumnType("decimal(4,2)");

            entity.Ignore(x => x.IsValid);

            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => x.IsActive);
        });

        // ── ProductReview ──────────────────────────────────────────
        builder.Entity<ProductReview>(entity =>
        {
            entity.HasKey(x => x.ReviewId);

            entity.Property(x => x.ReviewerUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ReviewerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ReviewerRole).HasMaxLength(50);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.Recommendation).HasMaxLength(20);

            entity.HasIndex(x => x.ProductId);
            entity.HasIndex(x => x.ReviewerUserId);
        });

        // ── WpPayment ─────────────────────────────────────────────
        builder.Entity<WpPayment>(entity =>
        {
            entity.HasKey(x => x.PaymentId);
            entity.Property(x => x.WorldpayTransactionId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PayerEmail).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.ResponseCode).HasMaxLength(50);
            entity.Property(x => x.CardLast4).HasMaxLength(4);
            entity.Property(x => x.CardBrand).HasMaxLength(50);
            entity.Property(x => x.PaymentTokenId).HasMaxLength(255);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasMany(x => x.Refunds)
                .WithOne(x => x.Payment)
                .HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.AuditLogs)
                .WithOne(x => x.Payment)
                .HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.WorldpayTransactionId).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        builder.Entity<WpPaymentRefund>(entity =>
        {
            entity.HasKey(x => x.RefundId);
            entity.Property(x => x.WorldpayRefundId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => x.WorldpayRefundId).IsUnique();
        });

        builder.Entity<WpPaymentAuditLog>(entity =>
        {
            entity.HasKey(x => x.LogId);
            entity.Property(x => x.Details).HasMaxLength(500);
            entity.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => x.PaymentId);
            entity.HasIndex(x => x.Timestamp);
        });

        // ── PayoutCycle ───────────────────────────────────────────
        builder.Entity<PayoutCycle>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.ApprovedByUserId).HasMaxLength(450);
            entity.Property(x => x.ApprovedByEmail).HasMaxLength(256);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasMany(x => x.Lines)
                .WithOne(x => x.Cycle)
                .HasForeignKey(x => x.CycleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.PeriodStart);
        });

        builder.Entity<ProviderPayoutLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ProviderEmail).HasMaxLength(256);
            entity.Property(x => x.ProviderName).HasMaxLength(200);
            entity.Property(x => x.ProviderRole).HasMaxLength(50);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(x => x.CycleId);
            entity.HasIndex(x => x.ProviderUserId);
        });

        // ── ServiceCategory ───────────────────────────────────────
        builder.Entity<ServiceCategory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(50).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => x.IsActive);
        });

        // ── Service (category FK nullable — existing rows unaffected) ─
        builder.Entity<Service>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Price).HasColumnType("decimal(10,2)");
            entity.HasOne(x => x.Category)
                  .WithMany(c => c.Services)
                  .HasForeignKey(x => x.CategoryId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(x => x.AddOns)
                  .WithOne(a => a.Service)
                  .HasForeignKey(a => a.ServiceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.CategoryId);
            entity.HasIndex(x => x.Active);
        });

        // ── ServiceAddOn ──────────────────────────────────────────
        builder.Entity<ServiceAddOn>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Price).HasColumnType("decimal(10,2)");
            entity.HasIndex(x => new { x.ServiceId, x.IsActive });
            entity.HasIndex(x => x.SortOrder);
        });

        // ── StreamFlag ────────────────────────────────────────────────
        builder.Entity<StreamFlag>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FlaggedByUserId).HasMaxLength(450);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Action).HasMaxLength(50);
            entity.Property(x => x.ReviewedByUserId).HasMaxLength(450);
            entity.Property(x => x.ReviewedByName).HasMaxLength(200);
            entity.Property(x => x.ReviewNotes).HasMaxLength(1000);
            entity.HasOne(x => x.Stream)
                  .WithMany()
                  .HasForeignKey(x => x.StreamId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.StreamId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.FlaggedAt);
        });

        // ── ServiceRequiredProduct ────────────────────────────────────
        builder.Entity<ServiceRequiredProduct>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasOne(x => x.Service)
                  .WithMany(s => s.RequiredProducts)
                  .HasForeignKey(x => x.ServiceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product)
                  .WithMany()
                  .HasForeignKey(x => x.ProductId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ServiceId, x.IsActive });
            entity.HasIndex(x => x.ProductId);
        });

        // ── Expense ───────────────────────────────────────────────────
        builder.Entity<Expense>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SubmittedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.SubmittedByName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ReceiptUrl).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ReviewedByUserId).HasMaxLength(450);
            entity.Property(x => x.ReviewedByName).HasMaxLength(200);
            entity.Property(x => x.ReviewNotes).HasMaxLength(500);
            entity.HasIndex(x => x.SubmittedByUserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.ExpenseDate);
            entity.HasIndex(x => x.Category);
        });

        // ── ArtistSubscription ────────────────────────────────────────
        builder.Entity<ArtistSubscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.MonthlyAmount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.LastBilledAmount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.NextBillingDate);
        });
    }
}
