using System.Net;
using System.Net.Http.Json;
using BadalBooking.Api.Domain;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class AcceptAndCancelTests(ApiFactory api)
{
    [Fact]
    public async Task Available_list_shows_paid_unaccepted_bookings_without_customer_data()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var unpaid = await alice.CreateBookingAsync();
        var paid = await alice.CreatePaidBookingAsync();

        var response = await performer.Client.GetAsync("/bookings/available");
        var items = (await response.Json()).EnumerateArray().ToList();

        Assert.Contains(items, b => b.GetProperty("id").GetGuid() == paid);
        Assert.DoesNotContain(items, b => b.GetProperty("id").GetGuid() == unpaid);
        var fields = items.First().EnumerateObject().Select(p => p.Name).Order();
        Assert.Equal(["createdAtUtc", "id", "pilgrimName"], fields);
    }

    [Fact]
    public async Task Ten_performers_accept_at_once_and_exactly_one_wins()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreatePaidBookingAsync();
        var performers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.NewPerformerAsync()));

        var results = await TestApi.RaceAsync(performers
            .Select(p => (Func<Task<HttpResponseMessage>>)(() => p.AcceptAsync(booking))).ToArray());

        Assert.Equal(1, results.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(9, results.Count(s => s == HttpStatusCode.Conflict));
        await using var db = api.CreateDbContext();
        var stored = await db.Bookings.SingleAsync(b => b.Id == booking);
        Assert.Equal(BookingStatus.Accepted, stored.Status);
        Assert.Contains(stored.PerformerId!.Value, performers.Select(p => p.UserId));
    }

    [Fact]
    public async Task Performer_sees_only_the_bookings_they_accepted()
    {
        var alice = await api.RegisterCustomerAsync();
        var me = await api.NewPerformerAsync();
        var other = await api.NewPerformerAsync();
        var mine = await alice.CreatePaidBookingAsync();
        var theirs = await alice.CreatePaidBookingAsync();
        (await me.AcceptAsync(mine)).EnsureSuccessStatusCode();
        (await other.AcceptAsync(theirs)).EnsureSuccessStatusCode();

        var list = (await (await me.Client.GetAsync("/performer/bookings")).Json()).EnumerateArray()
            .Select(b => b.GetProperty("id").GetGuid()).ToList();

        Assert.Equal([mine], list);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.GetAsync("/performer/bookings")).StatusCode);
    }

    [Fact]
    public async Task Unpaid_booking_cannot_be_accepted()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var booking = await alice.CreateBookingAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await performer.AcceptAsync(booking)).StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_accept_a_booking()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreatePaidBookingAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.PostAsync($"/bookings/{booking}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Cancelling_an_unpaid_booking_needs_no_refund()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();

        var response = await alice.CancelAsync(booking);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Json()).GetProperty("refunded").GetBoolean());
    }

    [Fact]
    public async Task Cancelling_a_paid_booking_refunds_the_payment_in_the_same_transaction()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreatePaidBookingAsync();

        var response = await alice.CancelAsync(booking);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Json()).GetProperty("refunded").GetBoolean());
        await using var db = api.CreateDbContext();
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.SingleAsync(b => b.Id == booking)).Status);
        var payment = await db.Payments.SingleAsync(p => p.BookingId == booking);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.StartsWith("SIMREF-", payment.RefundReference);
    }

    [Fact]
    public async Task Accepted_booking_cannot_be_cancelled_and_cancelled_cannot_be_accepted()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var accepted = await alice.CreatePaidBookingAsync();
        var cancelled = await alice.CreatePaidBookingAsync();
        (await performer.AcceptAsync(accepted)).EnsureSuccessStatusCode();
        (await alice.CancelAsync(cancelled)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await alice.CancelAsync(accepted)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await performer.AcceptAsync(cancelled)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.CancelAsync(cancelled)).StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_cancel_another_customers_booking()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();
        var booking = await alice.CreatePaidBookingAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.CancelAsync(booking)).StatusCode);
        await using var db = api.CreateDbContext();
        Assert.Equal(BookingStatus.Paid, (await db.Bookings.SingleAsync(b => b.Id == booking)).Status);
    }

    // Cancel and accept at the same moment: exactly one may win, and the payment must match the winner.
    [Fact]
    public async Task Cancel_and_accept_racing_never_both_succeed()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();

        for (var round = 0; round < 15; round++)
        {
            var booking = await alice.CreatePaidBookingAsync();

            var results = await TestApi.RaceAsync(
                () => alice.CancelAsync(booking),
                () => performer.AcceptAsync(booking));

            Assert.Equal(1, results.Count(s => s == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(s => s == HttpStatusCode.Conflict));

            await using var db = api.CreateDbContext();
            var stored = await db.Bookings.SingleAsync(b => b.Id == booking);
            var payment = await db.Payments.SingleAsync(p => p.BookingId == booking);
            if (stored.Status == BookingStatus.Accepted)
            {
                Assert.Equal(performer.UserId, stored.PerformerId);
                Assert.Equal(PaymentStatus.Succeeded, payment.Status);
            }
            else
            {
                Assert.Equal(BookingStatus.Cancelled, stored.Status);
                Assert.Null(stored.PerformerId);
                Assert.Equal(PaymentStatus.Refunded, payment.Status);
            }
        }
    }
}
