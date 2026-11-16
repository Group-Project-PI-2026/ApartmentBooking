using ApartmentBookingDomain.Enums;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBookingDomain.Entities
{
    public class Booking
    {
        public Guid Id { get; private set; }
        public Guid ApartmentId { get; private set; }
        public Guid TenantId { get; private set; }
        public string? TenantEmail { get; private set; }
        public DateRange Dates { get; private set; } = null!;
        public decimal TotalPrice { get; private set; }
        public decimal PaidAmount { get; private set; }
        public BookingStatus Status { get; private set; }
        public DateTime CreatedAt { get; private set; }

        public DateTime? CancelledAt { get; private set; }
        public decimal PenaltyAmount { get; private set; }
        public decimal RefundAmount { get; private set; }
        public RefundStatus RefundStatus { get; private set; }

        public decimal BalanceDue => Math.Max(0, TotalPrice - PaidAmount);

        private Booking() { }

        //created only after it was actually paid (at least the prepayment)
        public Booking(Guid apartmentId, Guid tenantId, string? tenantEmail, DateRange dates, decimal totalPrice, decimal initialPayment)
        {
            if (totalPrice < 0)
                throw new DomainException("Total price cannot be negative.");
            if (initialPayment <= 0)
                throw new DomainException("Booking must be created with a positive payment.");

            Id = Guid.NewGuid();
            ApartmentId = apartmentId;
            TenantId = tenantId;
            TenantEmail = tenantEmail;
            Dates = dates;
            TotalPrice = totalPrice;
            PaidAmount = initialPayment;
            Status = PaidAmount >= TotalPrice ? BookingStatus.Paid : BookingStatus.PartiallyPaid;
            RefundStatus = RefundStatus.None;
            CreatedAt = DateTime.UtcNow;
        }

        public void RegisterPayment(decimal amount)
        {
            if (amount <= 0)
                throw new DomainException("Payment amount must be positive.");
            if (Status == BookingStatus.Cancelled)
                throw new DomainException("Cannot pay for a cancelled booking.");

            PaidAmount += amount;
            if (PaidAmount >= TotalPrice)
                Status = BookingStatus.Paid;
        }

        public void Cancel(decimal penaltyAmount, decimal refundAmount)
        {
            if (Status == BookingStatus.Cancelled)
                throw new DomainException("Booking is already cancelled.");
            if (penaltyAmount < 0 || refundAmount < 0)
                throw new DomainException("Penalty and refund amounts cannot be negative.");
            if (penaltyAmount + refundAmount > PaidAmount)
                throw new DomainException("Penalty and refund cannot exceed the paid amount.");

            Status = BookingStatus.Cancelled;
            CancelledAt = DateTime.UtcNow;
            PenaltyAmount = penaltyAmount;
            RefundAmount = refundAmount;
            RefundStatus = refundAmount > 0 ? RefundStatus.Pending : RefundStatus.None;
        }

        public void MarkRefundSucceeded()
        {
            RefundStatus = RefundStatus.Succeeded;
        }

        public void MarkRefundFailed()
        {
            RefundStatus = RefundStatus.Failed;
        }
    }
}
