using System.Net;
using ApartmentBooking.Tests.Api.Infrastructure;
using ApartmentBookingApplication.Bookings;
using ApartmentBookingDomain.Enums;
using Microsoft.EntityFrameworkCore;
using static ApartmentBooking.Tests.Api.Infrastructure.ApiHelpers;

namespace ApartmentBooking.Tests.Api;

/// <summary>Integration: Stripe webhook endpoint (TC-PAY-002..009).</summary>
[Collection(ApiCollection.Name)]
[Trait("Level", "Integration")]
public class WebhooksApiTests(ApiFactory api)
{
    private readonly HttpClient _stripe = api.ClientFor(null);

    private async Task<(TestUser Tenant, Guid ApartmentId, ReserveBookingResponse Reservation, FakeStripeGateway.CreatedSession Session)>
        ReserveAsync(int daysAhead, int nights = 2, decimal price = 1_000m)
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync(pricePerNight: price);
        var checkIn = Today.AddDays(daysAhead);
        var reservation = await (await (await api.ClientFor(tenant).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(nights)))
            .EnsureStatusAsync(HttpStatusCode.OK)).ReadAsync<ReserveBookingResponse>();
        return (tenant, apartment.Id, reservation, api.Stripe.SessionFor("lock_id", reservation.LockId.ToString()));
    }

    [ApiFact]
    public async Task ValidWebhook_CreatesBookingAndPayment_RemovesLock_AndSendsConfirmation()
    {
        var (tenant, apartmentId, reservation, session) = await ReserveAsync(20);

        var response = await _stripe.SendWebhookAsync(CompletedEvent(session));

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var booking = await api.WithDbAsync(db => db.Bookings.SingleAsync(b => b.ApartmentId == apartmentId));
        Assert.Equal(tenant.Id, booking.TenantId);
        Assert.Equal(BookingStatus.PartiallyPaid, booking.Status);
        Assert.Equal(reservation.AmountToPay, booking.PaidAmount);
        var payment = await api.WithDbAsync(db => db.Payments.SingleAsync(p => p.BookingId == booking.Id));
        Assert.Equal(session.SessionId, payment.CheckoutSessionId);
        Assert.Equal(PaymentType.Reservation, payment.Type);
        Assert.False(await api.WithDbAsync(db => db.BookingLocks.AnyAsync(l => l.Id == reservation.LockId)));
        Assert.Contains(api.Email.Messages, m => m.To == tenant.Email && m.Subject == "Бронювання підтверджено");
    }

    [ApiTheory]
    [InlineData(null)]
    [InlineData("t=1,v1=forged")]
    public async Task InvalidOrMissingSignature_Returns400_AndChangesNothing(string? signature)
    {
        var (_, apartmentId, reservation, session) = await ReserveAsync(21);

        var response = await _stripe.SendWebhookAsync(CompletedEvent(session), signature);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Bookings.AnyAsync(b => b.ApartmentId == apartmentId)));
        Assert.True(await api.WithDbAsync(db => db.BookingLocks.AnyAsync(l => l.Id == reservation.LockId)));
    }

    [ApiFact]
    public async Task DuplicateDelivery_IsIdempotent()
    {
        var (_, apartmentId, _, session) = await ReserveAsync(22);
        var evt = CompletedEvent(session);

        await (await _stripe.SendWebhookAsync(evt)).EnsureStatusAsync(HttpStatusCode.OK);
        await (await _stripe.SendWebhookAsync(evt)).EnsureStatusAsync(HttpStatusCode.OK);
        await (await _stripe.SendWebhookAsync(evt with { EventId = "evt_retry" })).EnsureStatusAsync(HttpStatusCode.OK);

        Assert.Equal(1, await api.WithDbAsync(db => db.Bookings.CountAsync(b => b.ApartmentId == apartmentId)));
        Assert.Equal(1, await api.WithDbAsync(db => db.Payments.CountAsync(p => p.CheckoutSessionId == session.SessionId)));
    }

    [ApiTheory]
    [InlineData("checkout.session.expired", "paid")]
    [InlineData("checkout.session.completed", "unpaid")]
    [InlineData("payment_intent.succeeded", "paid")]
    public async Task IrrelevantOrUnpaidEvents_AreAcknowledgedAndIgnored(string type, string status)
    {
        var (_, apartmentId, _, session) = await ReserveAsync(23);

        var response = await _stripe.SendWebhookAsync(CompletedEvent(session, type: type, paymentStatus: status));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Bookings.AnyAsync(b => b.ApartmentId == apartmentId)));
    }

    [ApiFact]
    public async Task LateWebhook_DatesStillFree_CreatesBookingAnyway()
    {
        var (_, apartmentId, reservation, session) = await ReserveAsync(24);
        await ExpireLockAsync(reservation.LockId);

        await (await _stripe.SendWebhookAsync(CompletedEvent(session))).EnsureStatusAsync(HttpStatusCode.OK);

        Assert.True(await api.WithDbAsync(db => db.Bookings.AnyAsync(b => b.ApartmentId == apartmentId)));
    }

    [ApiFact]
    public async Task LateWebhook_DatesTakenByAnotherTenant_RefundsInsteadOfDoubleBooking()
    {
        var (lateTenant, apartmentId, reservation, lateSession) = await ReserveAsync(25);
        await ExpireLockAsync(reservation.LockId);
        var checkIn = Today.AddDays(25);
        await api.BookAsync(TestUser.Tenant(), apartmentId, checkIn, checkIn.AddDays(2));

        await (await _stripe.SendWebhookAsync(CompletedEvent(lateSession))).EnsureStatusAsync(HttpStatusCode.OK);

        var bookings = await api.WithDbAsync(db => db.Bookings.Where(b => b.ApartmentId == apartmentId).ToListAsync());
        Assert.Single(bookings);
        Assert.DoesNotContain(bookings, b => b.TenantId == lateTenant.Id);
        Assert.Contains(api.Stripe.Refunds, r => r.IdempotencyKey == $"refund-session-{lateSession.SessionId}" && r.Amount == lateSession.Request.Amount);
    }

    [ApiFact]
    public async Task BalanceWebhook_CompletesPayment_StatusBecomesPaid()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync(pricePerNight: 1_000m);
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(30), Today.AddDays(32));
        await (await api.ClientFor(tenant).PostAsync($"/api/bookings/{bookingId}/pay-balance", null)).EnsureStatusAsync(HttpStatusCode.OK);
        var session = api.Stripe.SessionFor("booking_id", bookingId.ToString());

        await (await _stripe.SendWebhookAsync(CompletedEvent(session))).EnsureStatusAsync(HttpStatusCode.OK);

        var details = await (await api.ClientFor(tenant).GetAsync($"/api/bookings/{bookingId}")).ReadAsync<BookingDetailsDto>();
        Assert.Equal(BookingStatus.Paid, details.Status);
        Assert.Equal(0m, details.BalanceDue);
        Assert.False(details.CanPayBalance);
        Assert.Equal(2, await api.WithDbAsync(db => db.Payments.CountAsync(p => p.BookingId == bookingId)));
    }

    [ApiFact]
    public async Task BalanceWebhook_ForCancelledBooking_IsRefunded()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync();
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(33), Today.AddDays(35));
        var client = api.ClientFor(tenant);
        await client.PostAsync($"/api/bookings/{bookingId}/pay-balance", null);
        var session = api.Stripe.SessionFor("booking_id", bookingId.ToString());
        await (await client.PostAsync($"/api/bookings/{bookingId}/cancel", null)).EnsureStatusAsync(HttpStatusCode.OK);

        await (await _stripe.SendWebhookAsync(CompletedEvent(session))).EnsureStatusAsync(HttpStatusCode.OK);

        Assert.Contains(api.Stripe.Refunds, r => r.IdempotencyKey == $"refund-session-{session.SessionId}");
        var booking = await api.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == bookingId));
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [ApiFact]
    [Trait("Category", "KnownBug")]
    [Trait("Bug", "BUG-BE-002")]
    public async Task BalanceWebhook_WithAmountAboveBalance_IsNotAcceptedAsOverpayment()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync(pricePerNight: 1_000m);
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(36), Today.AddDays(37));
        await api.ClientFor(tenant).PostAsync($"/api/bookings/{bookingId}/pay-balance", null);
        var session = api.Stripe.SessionFor("booking_id", bookingId.ToString());

        await _stripe.SendWebhookAsync(CompletedEvent(session, amount: 5_000m));

        var booking = await api.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == bookingId));
        Assert.True(booking.PaidAmount <= booking.TotalPrice, $"PaidAmount {booking.PaidAmount} > TotalPrice {booking.TotalPrice}");
    }

    [ApiFact]
    [Trait("Category", "KnownBug")]
    [Trait("Bug", "BUG-BE-004")]
    public async Task ReservationWebhook_WithAmountBelowLockAmount_DoesNotConfirmBooking()
    {
        var (_, apartmentId, reservation, session) = await ReserveAsync(38);

        await _stripe.SendWebhookAsync(CompletedEvent(session, amount: 1m));

        // Expected: amount is reconciled with booking_lock.AmountToPay (900) and the booking is not confirmed for 1 UAH.
        var booking = await api.WithDbAsync(db => db.Bookings.SingleOrDefaultAsync(b => b.ApartmentId == apartmentId));
        Assert.True(booking is null || booking.PaidAmount == reservation.AmountToPay,
            $"Booking confirmed with PaidAmount={booking?.PaidAmount}, expected {reservation.AmountToPay}");
    }

    [ApiFact]
    [Trait("Category", "KnownBug")]
    [Trait("Bug", "BUG-BE-005")]
    public async Task ReservationWebhook_WithMalformedMetadata_DoesNotReturn500()
    {
        var (_, _, _, session) = await ReserveAsync(39);
        var broken = new Dictionary<string, string>(session.Request.Metadata) { ["lock_id"] = "not-a-guid" };

        var response = await _stripe.SendWebhookAsync(CompletedEvent(session, metadata: broken));

        // Expected: 4xx/2xx with a logged warning. Actual: FormatException -> 500, Stripe keeps retrying for 3 days.
        Assert.True((int)response.StatusCode < 500, $"Got {(int)response.StatusCode}");
    }

    private Task<int> ExpireLockAsync(Guid lockId) =>
        api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE booking_locks SET expires_at = now() - interval '1 minute' WHERE id = {lockId}"));
}
