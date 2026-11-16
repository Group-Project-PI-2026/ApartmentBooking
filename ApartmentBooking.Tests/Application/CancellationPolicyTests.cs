using ApartmentBookingApplication.Bookings;
using ApartmentBookingApplication.Options;
using ApartmentBookingDomain.Entities;

namespace ApartmentBooking.Tests.Application;

public class CancellationPolicyTests
{
    private static readonly BookingOptions Options = new()
    {
        FreeCancellationDays = 7,
        LateCancellationPenaltyPercent = 50m
    };

    [Fact]
    public void Calculate_OnFreeCancellationBoundary_ReturnsFullRefund()
    {
        var today = new DateOnly(2027, 3, 1);
        var booking = CreateBooking(checkIn: today.AddDays(7), totalPrice: 1_000m, paidAmount: 300m);

        var result = CancellationPolicy.Calculate(booking, today, Options);

        Assert.True(result.CanCancel);
        Assert.Equal(0m, result.PenaltyAmount);
        Assert.Equal(300m, result.RefundAmount);
    }

    [Fact]
    public void Calculate_InsideLateWindow_AppliesConfiguredPenalty()
    {
        var today = new DateOnly(2027, 3, 1);
        var booking = CreateBooking(checkIn: today.AddDays(6), totalPrice: 1_000m, paidAmount: 1_000m);

        var result = CancellationPolicy.Calculate(booking, today, Options);

        Assert.True(result.CanCancel);
        Assert.Equal(500m, result.PenaltyAmount);
        Assert.Equal(500m, result.RefundAmount);
    }

    [Fact]
    public void Calculate_WhenDepositIsLessThanPenalty_CapsPenaltyAtPaidAmount()
    {
        var today = new DateOnly(2027, 3, 1);
        var booking = CreateBooking(checkIn: today.AddDays(1), totalPrice: 1_000m, paidAmount: 300m);

        var result = CancellationPolicy.Calculate(booking, today, Options);

        Assert.Equal(300m, result.PenaltyAmount);
        Assert.Equal(0m, result.RefundAmount);
    }

    [Fact]
    public void Calculate_OnCheckinDay_DeniesCancellation()
    {
        var today = new DateOnly(2027, 3, 1);
        var booking = CreateBooking(checkIn: today, totalPrice: 1_000m, paidAmount: 1_000m);

        var result = CancellationPolicy.Calculate(booking, today, Options);

        Assert.False(result.CanCancel);
        Assert.Contains("already started", result.Reason);
    }

    [Fact]
    public void Calculate_ForCancelledBooking_DeniesCancellation()
    {
        var today = new DateOnly(2027, 3, 1);
        var booking = CreateBooking(checkIn: today.AddDays(10), totalPrice: 1_000m, paidAmount: 1_000m);
        booking.Cancel(0m, 1_000m);

        var result = CancellationPolicy.Calculate(booking, today, Options);

        Assert.False(result.CanCancel);
        Assert.Contains("already cancelled", result.Reason);
    }

    private static Booking CreateBooking(DateOnly checkIn, decimal totalPrice, decimal paidAmount) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "tenant@example.test",
            new DateRange(checkIn, checkIn.AddDays(2)),
            totalPrice,
            paidAmount);
}
