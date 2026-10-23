using System.Net;
using BadalBooking.Api.Background;
using BadalBooking.Api.Domain;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BadalBooking.Tests;

// The booking change and its queued notification must be saved together, or not at all.
[Collection(TestCollection.Name)]
public class OutboxTests(ApiFactory api)
{
    private async Task<List<OutboxMessage>> EventsFor(Guid bookingId)
    {
        await using var db = api.CreateDbContext();
        return await db.Outbox.Where(o => o.BookingId == bookingId).ToListAsync();
    }

    private async Task RunExpiryAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BookingExpiryService>().ExpireDueAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Accepting_queues_exactly_one_notification_for_the_customer()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var booking = await alice.CreatePaidBookingAsync();

        (await performer.AcceptAsync(booking)).EnsureSuccessStatusCode();

        var evt = Assert.Single(await EventsFor(booking));
        Assert.Equal(NotificationTypes.BookingAccepted, evt.Type);
        Assert.Equal(alice.UserId, evt.UserId); // the CUSTOMER is notified, not the performer
        Assert.Null(evt.ProcessedAtUtc);        // waiting for the worker (step 2.6)
    }

    [Fact]
    public async Task Racing_accepts_queue_only_one_notification()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreatePaidBookingAsync();
        var performers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.NewPerformerAsync()));

        await TestApi.RaceAsync(performers.Select(p => (Func<Task<HttpResponseMessage>>)(() => p.AcceptAsync(booking))).ToArray());

        Assert.Single(await EventsFor(booking));
    }

    [Fact]
    public async Task Failed_accept_queues_nothing()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var unpaid = await alice.CreateBookingAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await performer.AcceptAsync(unpaid)).StatusCode);
        Assert.Empty(await EventsFor(unpaid));
    }

    [Fact]
    public async Task Expiry_queues_one_notification_per_expired_booking_and_none_on_rerun()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();
        api.Clock.Advance(TimeSpan.FromMinutes(31));

        await RunExpiryAsync();
        await RunExpiryAsync();

        var evt = Assert.Single(await EventsFor(booking));
        Assert.Equal(NotificationTypes.BookingExpired, evt.Type);
        Assert.Equal(alice.UserId, evt.UserId);
    }

    // Proves atomicity: if queuing the notification fails, the booking change is rolled back too.
    // We force the failure by planting a conflicting event first (unique booking_id + type).
    [Fact]
    public async Task If_the_notification_cannot_be_queued_the_accept_is_rolled_back()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var booking = await alice.CreatePaidBookingAsync();
        await using (var db = api.CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO app.notification_outbox (id, type, booking_id, user_id, created_at_utc, attempts, next_attempt_at_utc)
                VALUES (gen_random_uuid(), 'BookingAccepted', {booking}, {alice.UserId}, now(), 0, now())
                """);
        }

        var response = await performer.AcceptAsync(booking);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var check = api.CreateDbContext();
        var stored = await check.Bookings.SingleAsync(b => b.Id == booking);
        Assert.Equal(BookingStatus.Paid, stored.Status); // NOT accepted
        Assert.Null(stored.PerformerId);
    }
}
