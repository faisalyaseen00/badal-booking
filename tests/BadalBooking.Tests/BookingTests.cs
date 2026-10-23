using System.Net;
using System.Net.Http.Json;
using BadalBooking.Api.Domain;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class BookingTests(ApiFactory api)
{
    [Fact]
    public async Task Client_cannot_choose_price_status_or_owner()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();

        var response = await alice.Client.PostAsJsonAsync("/bookings", new
        {
            pilgrimName = "Abdullah",
            amountHalalas = 1,
            currency = "USD",
            status = "Accepted",
            customerId = bob.UserId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Json()).GetProperty("id").GetGuid();

        // Check the database itself, not just the response.
        await using var db = api.CreateDbContext();
        var stored = await db.Bookings.SingleAsync(b => b.Id == id);
        Assert.Equal(50_000, stored.AmountHalalas);
        Assert.Equal("SAR", stored.Currency);
        Assert.Equal(BookingStatus.PendingPayment, stored.Status);
        Assert.Equal(alice.UserId, stored.CustomerId);
    }

    [Fact]
    public async Task Customer_cannot_read_another_customers_booking()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();
        var alicesBooking = await alice.CreateBookingAsync();

        var asBob = await bob.Client.GetAsync($"/bookings/{alicesBooking}");
        var missing = await bob.Client.GetAsync($"/bookings/{Guid.NewGuid()}");

        // Same answer as "doesn't exist", so Bob can't even learn that the id is real.
        Assert.Equal(HttpStatusCode.NotFound, asBob.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/bookings/{alicesBooking}")).StatusCode);
    }

    [Fact]
    public async Task List_shows_only_my_bookings_newest_first()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();
        var first = await alice.CreateBookingAsync("First");
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await alice.CreateBookingAsync("Second");
        await bob.CreateBookingAsync("Bob's");

        var list = await alice.Client.GetFromJsonAsync<List<BookingRow>>("/bookings");

        Assert.Equal([second, first], list!.Select(b => b.Id));
    }

    [Fact]
    public async Task List_page_size_is_capped()
    {
        var alice = await api.RegisterCustomerAsync();
        for (var i = 0; i < 3; i++) await alice.CreateBookingAsync();

        var one = await alice.Client.GetFromJsonAsync<List<BookingRow>>("/bookings?limit=1");
        var huge = await alice.Client.GetAsync("/bookings?limit=999999");

        Assert.Single(one!);
        Assert.Equal(HttpStatusCode.OK, huge.StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_cancel_or_pay_another_customers_booking()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();
        var alicesBooking = await alice.CreateBookingAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.CancelAsync(alicesBooking)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PayAsync(alicesBooking)).StatusCode);

        await using var db = api.CreateDbContext();
        Assert.Equal(BookingStatus.PendingPayment, (await db.Bookings.SingleAsync(b => b.Id == alicesBooking)).Status);
        Assert.False(await db.Payments.AnyAsync(p => p.BookingId == alicesBooking));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Pilgrim_name_is_required(string? name)
    {
        var alice = await api.RegisterCustomerAsync();

        var response = await alice.Client.PostAsJsonAsync("/bookings", new { pilgrimName = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pilgrim_name_longer_than_200_characters_is_rejected()
    {
        var alice = await api.RegisterCustomerAsync();

        var response = await alice.Client.PostAsJsonAsync("/bookings", new { pilgrimName = new string('a', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record BookingRow(Guid Id, string PilgrimName, string Status);
}
