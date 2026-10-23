using System.Net;
using BadalBooking.Api.Domain;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class PaymentTests(ApiFactory api)
{
    [Fact]
    public async Task Paying_marks_the_booking_paid_with_one_simulated_payment_of_500_SAR()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();

        var response = await alice.PayAsync(booking);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Json();
        Assert.Equal(50_000, body.GetProperty("amountHalalas").GetInt32());
        Assert.True(body.GetProperty("simulated").GetBoolean());

        await using var db = api.CreateDbContext();
        Assert.Equal(BookingStatus.Paid, (await db.Bookings.SingleAsync(b => b.Id == booking)).Status);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.BookingId == booking));
    }

    [Fact]
    public async Task Same_idempotency_key_twice_returns_the_same_payment_and_charges_once()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();

        var first = await alice.PayAsync(booking, "key-1");
        var retry = await alice.PayAsync(booking, "key-1");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await first.Json()).GetProperty("id").GetGuid(), (await retry.Json()).GetProperty("id").GetGuid());
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());

        await using var db = api.CreateDbContext();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.BookingId == booking));
    }

    [Fact]
    public async Task A_new_key_on_an_already_paid_booking_returns_409()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreatePaidBookingAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await alice.PayAsync(booking, "another-key")).StatusCode);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_booking_returns_422()
    {
        var alice = await api.RegisterCustomerAsync();
        var b1 = await alice.CreateBookingAsync();
        var b2 = await alice.CreateBookingAsync();
        (await alice.PayAsync(b1, "shared-key")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PayAsync(b2, "shared-key")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("bad!chars")]
    public async Task Missing_or_invalid_idempotency_key_returns_400(string key)
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/bookings/{booking}/pay");
        if (key.Length > 0) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Ten_parallel_payments_with_different_keys_produce_exactly_one_payment()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();

        var results = await TestApi.RaceAsync(Enumerable.Range(0, 10)
            .Select(i => (Func<Task<HttpResponseMessage>>)(() => alice.PayAsync(booking, $"race-{i}"))).ToArray());

        Assert.Equal(1, results.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(9, results.Count(s => s == HttpStatusCode.Conflict));
        await using var db = api.CreateDbContext();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.BookingId == booking));
    }

    // Regression test for the bug found in the step 1.5 demo: some same-key retries got 409.
    [Fact]
    public async Task Ten_parallel_retries_with_the_same_key_all_get_the_same_payment()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreateBookingAsync();

        var responses = new HttpResponseMessage[10];
        await TestApi.RaceAsync(Enumerable.Range(0, 10).Select(i => (Func<Task<HttpResponseMessage>>)(async () =>
            responses[i] = await alice.PayAsync(booking, "same-key"))).ToArray());

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Json()).GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
        await using var db = api.CreateDbContext();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.BookingId == booking));
    }

    [Fact]
    public async Task Payment_is_rejected_once_the_30_minute_deadline_is_reached()
    {
        var alice = await api.RegisterCustomerAsync();
        var onTime = await alice.CreateBookingAsync();
        var late = await alice.CreateBookingAsync();

        api.Clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
        Assert.Equal(HttpStatusCode.OK, (await alice.PayAsync(onTime)).StatusCode);

        api.Clock.Advance(TimeSpan.FromSeconds(1)); // exactly 30:00 after creation
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PayAsync(late)).StatusCode);

        await using var db = api.CreateDbContext();
        Assert.Equal(BookingStatus.PendingPayment, (await db.Bookings.SingleAsync(b => b.Id == late)).Status);
    }

    [Fact]
    public async Task Database_refuses_a_second_successful_payment_even_if_code_is_bypassed()
    {
        var alice = await api.RegisterCustomerAsync();
        var booking = await alice.CreatePaidBookingAsync();

        await using var db = api.CreateDbContext();
        var sneak = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.payments (id, booking_id, customer_id, idempotency_key, amount_halalas, currency, status, provider, provider_reference, created_at_utc)
            SELECT gen_random_uuid(), booking_id, customer_id, 'sql-sneak', amount_halalas, currency, 'Succeeded', 'Simulated', 'x', now()
            FROM app.payments WHERE booking_id = {booking}
            """);

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(sneak);
        Assert.Equal("ux_payments_one_success_per_booking", error.ConstraintName);
    }
}
