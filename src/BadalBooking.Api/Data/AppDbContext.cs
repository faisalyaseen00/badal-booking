using BadalBooking.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Our own schema instead of "public": on Supabase the "public" schema is exposed
    // through its auto-generated REST API, so keeping tables out of it is safer.
    public const string Schema = "app";

    public DbSet<User> Users => Set<User>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<FirebaseIdentity> FirebaseIdentities => Set<FirebaseIdentity>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<PerformerApplication> PerformerApplications => Set<PerformerApplication>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);

        model.Entity<User>(user =>
        {
            user.Property(x => x.Email).HasMaxLength(256);
            user.HasIndex(x => x.Email).IsUnique();

            user.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            user.ToTable(t => t.HasCheckConstraint("ck_users_role_valid", "role IN ('Customer', 'Performer', 'Admin')"));
        });

        model.Entity<Booking>(booking =>
        {
            booking.Property(x => x.PilgrimName).HasMaxLength(Booking.PilgrimNameMaxLength);
            booking.Property(x => x.Currency).HasMaxLength(3);
            booking.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            booking.Property(x => x.Version).IsRowVersion();

            // Restrict: deleting a user must never silently delete their bookings.
            booking.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            booking.HasOne<User>().WithMany().HasForeignKey(x => x.PerformerId).OnDelete(DeleteBehavior.Restrict);

            booking.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc }); // "my bookings, newest first"
            booking.HasIndex(x => new { x.Status, x.PaymentDeadlineUtc }); // expiry job + available list

            // Last line of defence: the database itself rejects impossible data,
            // even if a future code change has a bug.
            booking.ToTable(t =>
            {
                t.HasCheckConstraint("ck_bookings_amount_positive", "amount_halalas > 0");
                t.HasCheckConstraint("ck_bookings_currency_sar", "currency = 'SAR'");
                t.HasCheckConstraint("ck_bookings_status_valid",
                    "status IN ('PendingPayment', 'Paid', 'Accepted', 'Cancelled', 'Expired')");
                // Accepted <=> has a performer. A booking can't be "Accepted" by nobody,
                // and can't have a performer while still pending, paid, cancelled or expired.
                t.HasCheckConstraint("ck_bookings_accepted_has_performer",
                    "(status = 'Accepted') = (performer_id IS NOT NULL)");
            });
        });

        model.Entity<FirebaseIdentity>(identity =>
        {
            identity.Property(x => x.FirebaseUid).HasMaxLength(128); // Firebase uids are at most 128 chars
            identity.HasIndex(x => x.FirebaseUid).IsUnique();
            identity.HasIndex(x => x.UserId).IsUnique();
            identity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<DeviceToken>(device =>
        {
            device.Property(x => x.Token).HasMaxLength(DeviceToken.TokenMaxLength);
            device.Property(x => x.Platform).HasMaxLength(20);
            device.HasIndex(x => x.Token).IsUnique();   // one owner per device token
            device.HasIndex(x => x.UserId);              // "all devices of this customer" for pushes
            device.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            device.ToTable(t => t.HasCheckConstraint("ck_device_tokens_platform_valid", "platform IN ('web', 'android', 'ios')"));
        });

        model.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("notification_outbox", t =>
                t.HasCheckConstraint("ck_outbox_type_valid", "type IN ('BookingAccepted', 'BookingExpired')"));
            outbox.Property(x => x.Type).HasMaxLength(40);
            outbox.Property(x => x.LastError).HasMaxLength(500);

            // Each booking can be accepted once and expire once, so one event per (booking, type).
            // If the same change were ever attempted twice, the database refuses the duplicate event.
            outbox.HasIndex(x => new { x.BookingId, x.Type }).IsUnique();

            // Fast lookup of "what still needs sending" (only unprocessed rows are indexed).
            outbox.HasIndex(x => x.NextAttemptAtUtc)
                .HasFilter("processed_at_utc IS NULL")
                .HasDatabaseName("ix_outbox_pending");

            outbox.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            outbox.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<PerformerApplication>(app =>
        {
            app.Property(x => x.FullName).HasMaxLength(PerformerApplication.FullNameMaxLength);
            app.Property(x => x.Phone).HasMaxLength(PerformerApplication.PhoneMaxLength);
            app.Property(x => x.Note).HasMaxLength(PerformerApplication.NoteMaxLength);
            app.Property(x => x.DecisionNote).HasMaxLength(PerformerApplication.DecisionNoteMaxLength);
            app.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            // One OPEN application per user (old approved/rejected ones don't count).
            app.HasIndex(x => x.UserId).IsUnique().HasFilter("status = 'Pending'")
                .HasDatabaseName("ux_performer_applications_one_pending_per_user");
            app.HasIndex(x => new { x.Status, x.CreatedAtUtc }); // admin list

            app.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            app.HasOne<User>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);

            app.ToTable(t =>
            {
                t.HasCheckConstraint("ck_performer_applications_status_valid", "status IN ('Pending', 'Approved', 'Rejected')");
                // Decided <=> has a decider and a time.
                t.HasCheckConstraint("ck_performer_applications_decision_fields",
                    "(status = 'Pending') = (decided_at_utc IS NULL AND decided_by_user_id IS NULL)");
            });
        });

        model.Entity<Payment>(payment =>
        {
            payment.Property(x => x.IdempotencyKey).HasMaxLength(Payment.IdempotencyKeyMaxLength);
            payment.Property(x => x.Currency).HasMaxLength(3);
            payment.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            payment.Property(x => x.Provider).HasMaxLength(30);
            payment.Property(x => x.ProviderReference).HasMaxLength(100);
            payment.Property(x => x.RefundReference).HasMaxLength(100);

            payment.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            payment.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

            // Same customer + same key = the same payment. A retry finds the stored row instead of charging again.
            payment.HasIndex(x => new { x.CustomerId, x.IdempotencyKey }).IsUnique();

            // At most ONE successful payment per booking, even if two requests use DIFFERENT keys.
            // (Partial unique index: only rows with status = 'Succeeded' count.)
            payment.HasIndex(x => x.BookingId)
                .IsUnique()
                .HasFilter("status = 'Succeeded'")
                .HasDatabaseName("ux_payments_one_success_per_booking");

            payment.ToTable(t =>
            {
                t.HasCheckConstraint("ck_payments_amount_positive", "amount_halalas > 0");
                t.HasCheckConstraint("ck_payments_currency_sar", "currency = 'SAR'");
                t.HasCheckConstraint("ck_payments_status_valid", "status IN ('Succeeded', 'Failed', 'Refunded')");
                // A refunded payment must say when and with which refund reference; others must not.
                t.HasCheckConstraint("ck_payments_refund_fields",
                    "(status = 'Refunded') = (refunded_at_utc IS NOT NULL AND refund_reference IS NOT NULL)");
            });
        });
    }
}
