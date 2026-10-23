using System.Net;
using BadalBooking.Api.Background;
using BadalBooking.Api.Domain;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BadalBooking.Tests;

// The worker's timer is off in tests. Each test moves the fake clock and runs one expiry pass directly,
// so the 30-minute rule is tested in milliseconds instead of waiting half an hour.
[Collection(TestCollection.Name)]
public class ExpiryTests(ApiFactory api)
{
    private async Task<IReadOnlyList<ExpiredBooking>> RunExpiryAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BookingExpiryService>().ExpireDueAsync(CancellationToken.None);
    }

    private async Task<BookingStatus> StatusOf(Guid id)
    {
        await using var db = api.CreateDbContext();
        return (await db.Bookings.SingleAsync(b => b.Id == id)).Status;
    }

    [Fact]
    public async Task Unpaid_booking_expires_at_exactly_30_minutes_and_not_before()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();

        api.Clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(await RunExpiryAsync(), e => e.Id == booking);
        Assert.Equal(BookingStatus.PendingPayment, await StatusOf(booking));

        api.Clock.Advance(TimeSpan.FromSeconds(1));
        var expired = await RunExpiryAsync();

        var mine = Assert.Single(expired, e => e.Id == booking);
        Assert.Equal(alice.UserId, mine.CustomerId); // step 2.5 needs this to notify the right customer
        Assert.Equal(BookingStatus.Expired, await StatusOf(booking));
    }

    [Fact]
    public async Task Paid_cancelled_and_accepted_bookings_are_never_expired()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var paid = await alice.CreatePaidBookingAsync();
        var cancelled = await alice.CreateBookingAsync();
        var accepted = await alice.CreatePaidBookingAsync();
        (await alice.CancelAsync(cancelled)).EnsureSuccessStatusCode();
        (await performer.AcceptAsync(accepted)).EnsureSuccessStatusCode();

        api.Clock.Advance(TimeSpan.FromHours(2));
        await RunExpiryAsync();

        Assert.Equal(BookingStatus.Paid, await StatusOf(paid));
        Assert.Equal(BookingStatus.Cancelled, await StatusOf(cancelled));
        Assert.Equal(BookingStatus.Accepted, await StatusOf(accepted));
    }

    [Fact]
    public async Task Running_expiry_twice_expires_each_booking_only_once()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();
        api.Clock.Advance(TimeSpan.FromMinutes(31));

        var first = await RunExpiryAsync();
        var second = await RunExpiryAsync();

        Assert.Contains(first, e => e.Id == booking);
        Assert.DoesNotContain(second, e => e.Id == booking);
    }

    [Fact]
    public async Task Expired_booking_cannot_be_paid_cancelled_or_accepted()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var booking = await alice.CreateBookingAsync();
        api.Clock.Advance(TimeSpan.FromMinutes(31));
        await RunExpiryAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await alice.PayAsync(booking)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.CancelAsync(booking)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await performer.AcceptAsync(booking)).StatusCode);
    }

    [Fact]
    public async Task Many_overdue_bookings_are_processed_in_batches_until_none_are_left()
    {
        var alice = await api.RegisterCustomerAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 120; i++) ids.Add(await alice.CreateBookingAsync()); // more than one batch of 100
        api.Clock.Advance(TimeSpan.FromMinutes(31));

        var expired = await RunExpiryAsync();

        Assert.All(ids, id => Assert.Contains(expired, e => e.Id == id));
        await using var db = api.CreateDbContext();
        Assert.Equal(0, await db.Bookings.CountAsync(b => ids.Contains(b.Id) && b.Status != BookingStatus.Expired));
    }
}
