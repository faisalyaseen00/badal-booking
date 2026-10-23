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

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);

        model.Entity<User>(user =>
        {
            user.Property(x => x.Email).HasMaxLength(256);
            user.HasIndex(x => x.Email).IsUnique();

            user.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            user.ToTable(t => t.HasCheckConstraint("ck_users_role_valid", "role IN ('Customer', 'Performer')"));
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
    }
}
