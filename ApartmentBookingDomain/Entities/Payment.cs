using ApartmentBookingDomain.Enums;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBookingDomain.Entities
{
    /// <summary>
    /// A successful Stripe payment that belongs to a booking. Needed to issue refunds
    /// (refunds are made against a PaymentIntent) and for webhook idempotency.
    /// </summary>
    public class Payment
    {
        public Guid Id { get; private set; }
        public Guid BookingId { get; private set; }
        public PaymentType Type { get; private set; }
        public decimal Amount { get; private set; }
        public decimal RefundedAmount { get; private set; }
        public string CheckoutSessionId { get; private set; }
        public string? PaymentIntentId { get; private set; }
        public DateTime CreatedAt { get; private set; }

        public decimal RefundableAmount => Amount - RefundedAmount;

        private Payment()
        {
            CheckoutSessionId = string.Empty;
        }

        public Payment(Guid bookingId, PaymentType type, decimal amount, string checkoutSessionId, string? paymentIntentId)
        {
            if (amount <= 0)
                throw new DomainException("Payment amount must be positive.");

            Id = Guid.NewGuid();
            BookingId = bookingId;
            Type = type;
            Amount = amount;
            CheckoutSessionId = checkoutSessionId;
            PaymentIntentId = paymentIntentId;
            CreatedAt = DateTime.UtcNow;
        }

        public void RegisterRefund(decimal amount)
        {
            if (amount <= 0 || amount > RefundableAmount)
                throw new DomainException("Invalid refund amount.");

            RefundedAmount += amount;
        }
    }
}
