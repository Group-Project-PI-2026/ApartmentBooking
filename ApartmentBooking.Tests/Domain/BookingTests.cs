using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBooking.Tests.Domain;

public class BookingTests
{
    [Fact]
    public void Constructor_WithDeposit_CreatesPartiallyPaidBooking()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 300m);

        Assert.Equal(BookingStatus.PartiallyPaid, booking.Status);
        Assert.Equal(300m, booking.PaidAmount);
        Assert.Equal(700m, booking.BalanceDue);
    }

    [Fact]
    public void Constructor_WithFullPayment_CreatesPaidBooking()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 1_000m);

        Assert.Equal(BookingStatus.Paid, booking.Status);
        Assert.Equal(0m, booking.BalanceDue);
    }

    [Fact]
    public void RegisterPayment_WithRemainingBalance_MarksBookingPaid()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 300m);

        booking.RegisterPayment(700m);

        Assert.Equal(BookingStatus.Paid, booking.Status);
        Assert.Equal(1_000m, booking.PaidAmount);
        Assert.Equal(0m, booking.BalanceDue);
    }

    [Fact]
    [Trait("Requirement", "Payment amount integrity")]
    public void RegisterPayment_WhenAmountExceedsBalance_ThrowsDomainException()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 300m);

        Assert.Throws<DomainException>(() => booking.RegisterPayment(701m));
    }

    [Fact]
    public void RegisterPayment_WithNonPositiveAmount_ThrowsDomainException()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 300m);

        Assert.Throws<DomainException>(() => booking.RegisterPayment(0m));
    }

    [Fact]
    public void Cancel_StoresPenaltyRefundAndPendingStatus()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 1_000m);

        booking.Cancel(penaltyAmount: 250m, refundAmount: 750m);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(250m, booking.PenaltyAmount);
        Assert.Equal(750m, booking.RefundAmount);
        Assert.Equal(RefundStatus.Pending, booking.RefundStatus);
        Assert.NotNull(booking.CancelledAt);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ThrowsDomainException()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 1_000m);
        booking.Cancel(0m, 1_000m);

        Assert.Throws<DomainException>(() => booking.Cancel(0m, 1_000m));
    }

    [Fact]
    public void RefundMarkers_UpdateRefundStatus()
    {
        var booking = CreateBooking(totalPrice: 1_000m, initialPayment: 1_000m);
        booking.Cancel(0m, 1_000m);

        booking.MarkRefundSucceeded();
        Assert.Equal(RefundStatus.Succeeded, booking.RefundStatus);

        booking.MarkRefundFailed();
        Assert.Equal(RefundStatus.Failed, booking.RefundStatus);
    }

    private static Booking CreateBooking(decimal totalPrice, decimal initialPayment) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "tenant@example.test",
            new DateRange(new DateOnly(2027, 3, 10), new DateOnly(2027, 3, 12)),
            totalPrice,
            initialPayment);
}
