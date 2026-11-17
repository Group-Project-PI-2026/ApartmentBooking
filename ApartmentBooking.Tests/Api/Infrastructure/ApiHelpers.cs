using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Bookings;
using ApartmentBookingApplication.OwnerRequests;

namespace ApartmentBooking.Tests.Api.Infrastructure;

internal static class ApiHelpers
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, ApiJson.Options);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, ApiJson.Options)
               ?? throw new InvalidOperationException($"Empty body, status {(int)response.StatusCode}");
    }

    public static async Task<HttpResponseMessage> EnsureStatusAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
        return response;
    }

    public static Task<HttpResponseMessage> ReserveAsync(
        this HttpClient client, Guid apartmentId, DateOnly checkIn, DateOnly checkOut, int guests = 2, bool payInFull = false) =>
        client.PostJsonAsync("/api/bookings/reserve", new ReserveBookingRequest
        {
            ApartmentId = apartmentId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            GuestsCount = guests,
            PayInFull = payInFull
        });

    public static PaymentWebhookEvent CompletedEvent(
        FakeStripeGateway.CreatedSession session,
        decimal? amount = null,
        string type = PaymentWebhookService.CheckoutSessionCompleted,
        string paymentStatus = "paid",
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            $"evt_{Guid.NewGuid():N}",
            type,
            session.SessionId,
            $"pi_{session.SessionId}",
            paymentStatus,
            amount ?? session.Request.Amount,
            session.Request.CustomerEmail,
            metadata ?? session.Request.Metadata);

    public static Task<HttpResponseMessage> SendWebhookAsync(
        this HttpClient client, PaymentWebhookEvent evt, string? signature = FakeStripeGateway.ValidSignature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(JsonSerializer.Serialize(evt, ApiJson.Options), Encoding.UTF8, "application/json")
        };
        if (signature is not null)
            request.Headers.Add("Stripe-Signature", signature);
        return client.SendAsync(request);
    }

    /// <summary>Reserve + pay through the (fake) Stripe webhook. Returns the created booking id.</summary>
    public static async Task<Guid> BookAsync(
        this ApiFactory api, TestUser tenant, Guid apartmentId, DateOnly checkIn, DateOnly checkOut, bool payInFull = false)
    {
        var client = api.ClientFor(tenant);
        var reserve = await (await client.ReserveAsync(apartmentId, checkIn, checkOut, payInFull: payInFull))
            .EnsureStatusAsync(System.Net.HttpStatusCode.OK);
        var reservation = await reserve.ReadAsync<ReserveBookingResponse>();

        var session = api.Stripe.SessionFor("lock_id", reservation.LockId.ToString());
        await (await api.CreateClient().SendWebhookAsync(CompletedEvent(session)))
            .EnsureStatusAsync(System.Net.HttpStatusCode.OK);

        var my = await (await client.GetAsync("/api/bookings/my")).ReadAsync<List<BookingListItemDto>>();
        return my.Single(b => b.ApartmentId == apartmentId && b.CheckIn == checkIn).Id;
    }

    public static CreateOwnerRequestRequest ValidOwnerRequest(string? title = null, decimal price = 1_500m) => new()
    {
        ContactName = "Олена Тестова",
        ContactEmail = $"owner-{Guid.NewGuid():N}@myrent.test",
        ContactPhone = "+380 67 123 45 67",
        DistrictId = 7,
        Address = "вул. Андріївський узвіз, 5, кв. 3",
        Title = title ?? $"Квартира {Guid.NewGuid():N}"[..20],
        Description = "Світла квартира на Подолі",
        MaxGuests = 3,
        ExpectedPricePerNight = price,
        Amenities = ["Wi-Fi", "Кухня"],
        PhotoUrls = []
    };
}
