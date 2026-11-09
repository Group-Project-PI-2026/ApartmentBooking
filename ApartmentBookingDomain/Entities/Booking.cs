using ApartmentBookingDomain.Enums;

namespace ApartmentBookingDomain.Entities
{
    public class Booking
    {
        public Guid Id { get; private set; }
        public Guid ApartmentId { get; private set; }
        public Guid TenantId { get; private set; }
        public DateRange Dates { get; private set; }
        public decimal TotalPrice { get; private set; }
        public BookingStatus Status { get; private set; }
        public DateTime CreatedAt { get; private set; }

        private Booking() { }
        //created only after it was actually paid
        public Booking(Guid apartmentId, Guid tenantId, DateRange dates, decimal totalPrice)
        {
            if (totalPrice < 0) throw new ArgumentException("Total price cannot be negative.");

            Id = Guid.NewGuid();
            ApartmentId = apartmentId;
            TenantId = tenantId;
            Dates = dates;
            TotalPrice = totalPrice;
            Status = BookingStatus.Paid;
            CreatedAt = DateTime.UtcNow;
        }

        public void Cancel()
        {
            if (Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking is already cancelled.");

            Status = BookingStatus.Cancelled;
        }
    }
}
