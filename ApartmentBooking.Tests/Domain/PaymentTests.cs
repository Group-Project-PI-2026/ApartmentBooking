using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBooking.Tests.Domain;

public class PaymentTests
{
    [Fact]
    public void Constructor_WithPositiveAmount_CreatesRefundablePayment()
    {
        var payment = CreatePayment(500m);

        Assert.Equal(500m, payment.Amount);
        Assert.Equal(500m, payment.RefundableAmount);
        Assert.Equal(0m, payment.RefundedAmount);
    }

    [Fact]
    public void Constructor_WithZeroAmount_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => CreatePayment(0m));
    }

    [Fact]
    public void RegisterRefund_ReducesRefundableAmount()
    {
        var payment = CreatePayment(500m);

        payment.RegisterRefund(125m);

        Assert.Equal(125m, payment.RefundedAmount);
        Assert.Equal(375m, payment.RefundableAmount);
    }

    [Fact]
    public void RegisterRefund_WhenAmountExceedsRefundable_ThrowsDomainException()
    {
        var payment = CreatePayment(500m);

        Assert.Throws<DomainException>(() => payment.RegisterRefund(501m));
    }

    private static Payment CreatePayment(decimal amount) =>
        new(Guid.NewGuid(), PaymentType.Reservation, amount, $"cs_{Guid.NewGuid():N}", $"pi_{Guid.NewGuid():N}");
}
