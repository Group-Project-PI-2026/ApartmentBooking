using System.ComponentModel.DataAnnotations;
using ApartmentBooking.Tests.TestDoubles;
using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Bookings;
using ApartmentBookingApplication.Exceptions;
using ApartmentBookingApplication.Options;
using ApartmentBookingApplication.OwnerRequests;
using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;
using ApartmentBookingDomain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApartmentBooking.Tests.Unit;

/// <summary>DataAnnotations contract of request DTOs (what ASP.NET Core returns as 400 before any service code runs).</summary>
[Trait("Level", "Unit")]
public class RequestValidationTests
{
    private static List<string> Errors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Reserve_GuestsOutOfRange_IsInvalid(int guests)
    {
        var request = new ReserveBookingRequest { ApartmentId = Guid.NewGuid(), GuestsCount = guests };

        Assert.Contains(nameof(ReserveBookingRequest.GuestsCount), Errors(request));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Reserve_GuestsOnBoundary_IsValid(int guests)
    {
        var request = new ReserveBookingRequest { ApartmentId = Guid.NewGuid(), GuestsCount = guests };

        Assert.Empty(Errors(request));
    }

    [Fact]
    public void OwnerRequest_Valid_HasNoErrors()
    {
        Assert.Empty(Errors(Valid()));
    }

    [Theory]
    [InlineData("ContactEmail", "user@")]
    [InlineData("ContactEmail", "")]
    [InlineData("ContactName", "")]
    [InlineData("ContactPhone", "")]
    [InlineData("Title", "")]
    public void OwnerRequest_RequiredOrFormatViolation_IsReported(string property, string value)
    {
        var request = Valid();
        typeof(CreateOwnerRequestRequest).GetProperty(property)!.SetValue(request, value);

        Assert.Contains(property, Errors(request));
    }

    [Fact]
    public void OwnerRequest_TitleLongerThan200_IsInvalid()
    {
        var request = Valid();
        request.Title = new string('x', 201);

        Assert.Contains(nameof(CreateOwnerRequestRequest.Title), Errors(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void OwnerRequest_MaxGuestsOutOfRange_IsInvalid(int guests)
    {
        var request = Valid();
        request.MaxGuests = guests;

        Assert.Contains(nameof(CreateOwnerRequestRequest.MaxGuests), Errors(request));
    }

    [Fact]
    public void Approve_NegativePrice_IsInvalid_NullPriceIsAllowed()
    {
        Assert.Contains("PricePerNight", Errors(new ApproveOwnerRequestRequest { PricePerNight = -1 }));
        Assert.Empty(Errors(new ApproveOwnerRequestRequest { PricePerNight = null }));
    }

    [Fact]
    public void RejectOrClarify_EmptyMessage_IsInvalid()
    {
        Assert.Contains("Message", Errors(new OwnerRequestMessageRequest { Message = "" }));
    }

    private static CreateOwnerRequestRequest Valid() => new()
    {
        ContactName = "Owner",
        ContactEmail = "owner@myrent.test",
        ContactPhone = "+380671234567",
        DistrictId = 1,
        Address = "Kyiv",
        Title = "Flat",
        MaxGuests = 2,
        ExpectedPricePerNight = 1000
    };
}

/// <summary>Webhook service branches that do not need a database (event filtering and signature errors).</summary>
[Trait("Level", "Unit")]
public class PaymentWebhookServiceUnitTests
{
    private static PaymentWebhookService Create(IPaymentGateway gateway) =>
        new(null!, gateway, new RecordingEmailSender(), Options.Create(new BookingOptions()), NullLogger<PaymentWebhookService>.Instance);

    private static PaymentWebhookEvent Event(string type, string? status, string? sessionId) =>
        new("evt_1", type, sessionId, "pi_1", status, 100m, null, new Dictionary<string, string>());

    [Theory]
    [InlineData("checkout.session.expired", "paid", "cs_1")]
    [InlineData("checkout.session.completed", "unpaid", "cs_1")]
    [InlineData("checkout.session.completed", null, "cs_1")]
    [InlineData("checkout.session.completed", "paid", null)]
    public async Task NotApplicableEvent_IsIgnored_WithoutTouchingDatabase(string type, string? status, string? sessionId)
    {
        var gateway = new RecordingPaymentGateway { WebhookEvent = Event(type, status, sessionId) };

        // The db is null: any access would throw NullReferenceException.
        await Create(gateway).HandleAsync("{}", "sig", CancellationToken.None);

        Assert.Empty(gateway.Refunds);
    }

    [Fact]
    public async Task InvalidSignature_FromGateway_Propagates()
    {
        var gateway = new ThrowingGateway();

        await Assert.ThrowsAsync<InvalidWebhookSignatureException>(
            () => Create(gateway).HandleAsync("{}", "bad", CancellationToken.None));
    }

    private sealed class ThrowingGateway : IPaymentGateway
    {
        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RefundAsync(string paymentIntentId, decimal amount, string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public PaymentWebhookEvent ParseWebhookEvent(string payload, string? signature) =>
            throw new InvalidWebhookSignatureException("bad signature");
    }
}

/// <summary>Extra domain rules not covered by the existing domain suite.</summary>
[Trait("Level", "Unit")]
public class DomainEdgeCaseTests
{
    private static readonly DateOnly Today = new(2027, 5, 10);

    [Fact]
    public void Booking_PartialPayment_HasBalanceDue_AndPartiallyPaidStatus()
    {
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), null, new DateRange(Today, Today.AddDays(2)), 2_000m, 600m);

        Assert.Equal(BookingStatus.PartiallyPaid, booking.Status);
        Assert.Equal(1_400m, booking.BalanceDue);
    }

    [Fact]
    public void Booking_PayingExactBalance_MakesItPaid()
    {
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), null, new DateRange(Today, Today.AddDays(2)), 2_000m, 600m);

        booking.RegisterPayment(1_400m);

        Assert.Equal(BookingStatus.Paid, booking.Status);
        Assert.Equal(0m, booking.BalanceDue);
    }

    [Fact]
    public void Booking_CancelWithoutRefund_HasRefundStatusNone()
    {
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), null, new DateRange(Today, Today.AddDays(2)), 2_000m, 600m);

        booking.Cancel(penaltyAmount: 600m, refundAmount: 0m);

        Assert.Equal(RefundStatus.None, booking.RefundStatus);
        Assert.NotNull(booking.CancelledAt);
    }

    [Fact]
    public void Booking_CannotPayAfterCancellation()
    {
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), null, new DateRange(Today, Today.AddDays(2)), 2_000m, 600m);
        booking.Cancel(0m, 600m);

        Assert.Throws<DomainException>(() => booking.RegisterPayment(100m));
    }

    [Fact]
    public void DateRange_CreateUpcoming_TodayIsAllowed_YesterdayIsNot()
    {
        Assert.Equal(1, DateRange.CreateUpcoming(Today, Today.AddDays(1), Today).Nights);
        Assert.Throws<DomainException>(() => DateRange.CreateUpcoming(Today.AddDays(-1), Today.AddDays(1), Today));
    }

    [Fact]
    public void BookingLock_ExpiresAfterFifteenMinutes()
    {
        var bookingLock = new BookingLock(Guid.NewGuid(), Guid.NewGuid(), null, new DateRange(Today, Today.AddDays(1)), 100m, 30m);

        Assert.Equal(TimeSpan.FromMinutes(15), bookingLock.ExpiresAt - bookingLock.CreatedAt);
        Assert.False(bookingLock.IsExpired());
    }

    [Fact]
    public void CancellationPolicy_AlreadyCancelled_IsDenied()
    {
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), null, new DateRange(Today.AddDays(30), Today.AddDays(32)), 2_000m, 600m);
        booking.Cancel(0m, 600m);

        var preview = CancellationPolicy.Calculate(booking, Today, new BookingOptions());

        Assert.False(preview.CanCancel);
        Assert.Equal("Booking is already cancelled.", preview.Reason);
    }

    [Fact]
    public void CancellationPolicy_UsesConfiguredOptions()
    {
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), null, new DateRange(Today.AddDays(10), Today.AddDays(12)), 2_000m, 2_000m);
        var strict = new BookingOptions { FreeCancellationDays = 14, LateCancellationPenaltyPercent = 25 };

        var preview = CancellationPolicy.Calculate(booking, Today, strict);

        Assert.Equal(500m, preview.PenaltyAmount);
        Assert.Equal(1_500m, preview.RefundAmount);
    }

    [Fact]
    public void Apartment_CalculatePrice_MultipliesNights()
    {
        var apartment = new Apartment(Guid.NewGuid(), 1, "T", "D", "A", 2, 1_250m);

        Assert.Equal(5_000m, apartment.CalculatePrice(new DateRange(Today, Today.AddDays(4))));
    }
}
