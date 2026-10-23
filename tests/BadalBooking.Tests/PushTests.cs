using System.Net.Http.Json;
using BadalBooking.Api.Background;
using BadalBooking.Api.Infrastructure.Push;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BadalBooking.Tests;

// The worker's timer is off in tests; each test runs one dispatch pass with a fake FCM.
[Collection(TestCollection.Name)]
public class PushTests(ApiFactory api)
{
    private async Task DispatchAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchDueAsync(CancellationToken.None);
    }

    private static async Task<string> RegisterDevice(TestApi.Session s)
    {
        var token = $"fcm-{Guid.NewGuid():N}";
        (await s.Client.PostAsJsonAsync("/devices", new { token, platform = "web" })).EnsureSuccessStatusCode();
        return token;
    }

    private async Task<Guid> AcceptedBookingFor(TestApi.Session customer)
    {
        var performer = await api.LoginSeededPerformerAsync();
        var booking = await customer.CreatePaidBookingAsync();
        (await performer.AcceptAsync(booking)).EnsureSuccessStatusCode();
        return booking;
    }

    [Fact]
    public async Task Accepted_booking_is_pushed_to_all_of_the_customers_devices_after_commit()
    {
        var alice = await api.RegisterCustomerAsync();
        var phone = await RegisterDevice(alice);
        var laptop = await RegisterDevice(alice);

        var booking = await AcceptedBookingFor(alice);
        Assert.Empty(api.Push.MessagesTo(phone)); // nothing sent inside the HTTP request

        await DispatchAsync();

        foreach (var device in new[] { phone, laptop })
        {
            var msg = Assert.Single(api.Push.MessagesTo(device));
            Assert.Equal("Booking accepted", msg.Title);
            Assert.Equal(booking.ToString(), msg.Data["bookingId"]);
            Assert.DoesNotContain("Pilgrim", msg.Body); // no personal data on the lock screen
        }
        await using var db = api.CreateDbContext();
        Assert.NotNull((await db.Outbox.SingleAsync(o => o.BookingId == booking)).ProcessedAtUtc);
    }

    [Fact]
    public async Task Expired_booking_is_pushed_once()
    {
        var alice = await api.RegisterCustomerAsync();
        var device = await RegisterDevice(alice);
        await alice.CreateBookingAsync();
        api.Clock.Advance(TimeSpan.FromMinutes(31));
        await using (var scope = api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BookingExpiryService>().ExpireDueAsync(CancellationToken.None);

        await DispatchAsync();
        await DispatchAsync();

        Assert.Equal("Booking expired", Assert.Single(api.Push.MessagesTo(device)).Title);
    }

    [Fact]
    public async Task Tokens_FCM_reports_as_invalid_are_deleted_and_others_kept()
    {
        var alice = await api.RegisterCustomerAsync();
        var good = await RegisterDevice(alice);
        var dead = await RegisterDevice(alice);
        api.Push.OutcomeByToken[dead] = PushOutcome.InvalidToken;

        await AcceptedBookingFor(alice);
        await DispatchAsync();

        await using var db = api.CreateDbContext();
        Assert.False(await db.DeviceTokens.AnyAsync(d => d.Token == dead));
        Assert.True(await db.DeviceTokens.AnyAsync(d => d.Token == good));
    }

    [Fact]
    public async Task Temporary_failures_keep_the_token_and_retry_later()
    {
        var alice = await api.RegisterCustomerAsync();
        var device = await RegisterDevice(alice);
        api.Push.OutcomeByToken[device] = PushOutcome.TemporaryFailure;

        var booking = await AcceptedBookingFor(alice);
        await DispatchAsync();

        await using (var db = api.CreateDbContext())
        {
            var row = await db.Outbox.SingleAsync(o => o.BookingId == booking);
            Assert.Null(row.ProcessedAtUtc);
            Assert.True(row.NextAttemptAtUtc > api.Clock.GetUtcNow().UtcDateTime); // backoff
            Assert.True(await db.DeviceTokens.AnyAsync(d => d.Token == device)); // token NOT deleted
        }

        api.Push.OutcomeByToken[device] = PushOutcome.Sent; // FCM recovers
        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await DispatchAsync();

        Assert.Single(api.Push.MessagesTo(device));
        await using var check = api.CreateDbContext();
        Assert.NotNull((await check.Outbox.SingleAsync(o => o.BookingId == booking)).ProcessedAtUtc);
    }

    [Fact]
    public async Task Device_logged_out_before_sending_gets_nothing()
    {
        var alice = await api.RegisterCustomerAsync();
        var device = await RegisterDevice(alice);
        await AcceptedBookingFor(alice);

        await alice.Client.PostAsJsonAsync("/auth/logout", new { deviceToken = device });
        await DispatchAsync();

        Assert.Empty(api.Push.MessagesTo(device));
    }

    [Fact]
    public async Task Customer_without_devices_is_marked_done_without_sending()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await AcceptedBookingFor(alice);

        await DispatchAsync();

        await using var db = api.CreateDbContext();
        var row = await db.Outbox.SingleAsync(o => o.BookingId == booking);
        Assert.NotNull(row.ProcessedAtUtc);
        Assert.Equal("no registered devices", row.LastError);
    }
}
