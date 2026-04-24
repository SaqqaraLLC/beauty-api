using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Beauty.Api.Models.Broadcasting;
using Beauty.Api.Models.Payments;
using Beauty.Api.Models.Products;
using Beauty.Api.Models.Streams;
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
    
    // Broadcasting
    public DbSet<BroadcastCampaign> BroadcastCampaigns => Set<BroadcastCampaign>();
    public DbSet<BroadcastSegment> BroadcastSegments => Set<BroadcastSegment>();
    public DbSet<BroadcastRecipient> BroadcastRecipients => Set<BroadcastRecipient>();
    public DbSet<BroadcastAuditLog> BroadcastAuditLogs => Set<BroadcastAuditLog>();

    // Payments
    public DbSet<Payment>         Payments       => Set<Payment>();
    public DbSet<PaymentRefund>   PaymentRefunds => Set<PaymentRefund>();
    public DbSet<PaymentAuditLog> PaymentAuditLogs => Set<PaymentAuditLog>();
    public DbSet<WebhookEvent>    WebhookEvents  => Set<WebhookEvent>();

    // Products
    public DbSet<Product>       Products       => Set<Product>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

    // Streams (Artist profiles and content)
    public DbSet<ArtistStream> ArtistStreams => Set<ArtistStream>();
    public DbSet<StreamDangerFlag> StreamDangerFlags => Set<StreamDangerFlag>();
    public DbSet<StreamReview> StreamReviews => Set<StreamReview>();
    public DbSet<StreamViewer> StreamViewers => Set<StreamViewer>();

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

        // Broadcasting Configuration
        builder.Entity<BroadcastCampaign>(entity =>
        {
            entity.HasKey(x => x.CampaignId);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Channel).HasConversion<string>();
            entity.HasMany(x => x.Recipients).WithOne(r => r.Campaign).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.AuditLogs).WithOne(a => a.Campaign).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BroadcastSegment>(entity =>
        {
            entity.HasKey(x => x.SegmentId);
            entity.Property(x => x.TargetRole).HasConversion<string?>();
            entity.HasMany(x => x.Campaigns).WithOne(c => c.Segment);
        });

        builder.Entity<BroadcastRecipient>(entity =>
        {
            entity.HasKey(x => x.RecipientId);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.HasIndex(x => new { x.CampaignId, x.RecipientEmail });
        });

        builder.Entity<BroadcastAuditLog>(entity =>
        {
            entity.HasKey(x => x.LogId);
            entity.Property(x => x.Action).HasConversion<string>();
        });

        // Payments Configuration
        builder.Entity<Payment>(entity =>
        {
            entity.HasKey(x => x.PaymentId);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.WorldpayTransactionId).HasMaxLength(100).IsRequired();
            entity.HasMany(x => x.Refunds).WithOne(r => r.Payment).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.WorldpayTransactionId).IsUnique();
            entity.HasIndex(x => new { x.BookingId, x.CreatedAt });
        });

        builder.Entity<PaymentRefund>(entity =>
        {
            entity.HasKey(x => x.RefundId);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.WorldpayRefundId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.WorldpayRefundId).IsUnique();
        });

        builder.Entity<PaymentAuditLog>(entity =>
        {
            entity.HasKey(x => x.LogId);
            entity.Property(x => x.Action).HasConversion<string>();
            entity.HasIndex(x => new { x.PaymentId, x.Timestamp });
        });

        builder.Entity<WebhookEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RawPayload).HasColumnType("longtext");
            entity.HasIndex(x => x.EventId).IsUnique();           // idempotency key
            entity.HasIndex(x => x.TransactionRef);               // fast lookup by transaction
            entity.HasIndex(x => new { x.Processed, x.ReceivedAt });
        });

        // Products Configuration
        builder.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.ProductId);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.HasMany(x => x.Reviews).WithOne(r => r.Product).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => new { x.VendorName, x.Status });
        });

        builder.Entity<ProductReview>(entity =>
        {
            entity.HasKey(x => x.ReviewId);
            entity.HasIndex(x => new { x.ProductId, x.ReviewedAt });
        });

        // Streams Configuration
        builder.Entity<ArtistStream>(entity =>
        {
            entity.HasKey(x => x.StreamId);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.HasMany(x => x.DangerFlags).WithOne(d => d.Stream).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Reviews).WithOne(r => r.Stream).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Viewers).WithOne(v => v.Stream).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ArtistId, x.CreatedAt });
            entity.HasIndex(x => x.IsFlaggedForReview);
        });

        builder.Entity<StreamDangerFlag>(entity =>
        {
            entity.HasKey(x => x.FlagId);
            entity.Property(x => x.DangerType).HasConversion<string>();
            entity.Property(x => x.ReviewStatus).HasConversion<string>();
            entity.Property(x => x.ActionTaken).HasConversion<string?>();
            entity.HasIndex(x => new { x.StreamId, x.ReviewStatus });
        });

        builder.Entity<StreamReview>(entity =>
        {
            entity.HasKey(x => x.ReviewId);
            entity.Property(x => x.Decision).HasConversion<string>();
            entity.HasIndex(x => new { x.StreamId, x.ReviewedAt });
        });

        builder.Entity<StreamViewer>(entity =>
        {
            entity.HasKey(x => x.ViewerId);
            entity.HasIndex(x => new { x.StreamId, x.ViewedAt });
        });
    }

    

}

