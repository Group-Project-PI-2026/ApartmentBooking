namespace ApartmentBookingDomain.Entities
{
    public class BookingLock
    {
        public Guid Id { get; private set; }
        public Guid ApartmentId { get; private set; }
        public Guid TenantId { get; private set; }
        public DateRange Dates { get; private set; }
        public DateTime ExpiresAt { get; private set; }

        private BookingLock() { }

        public BookingLock(Guid apartmentId, Guid tenantId, DateRange dates)
        {
            Id = Guid.NewGuid();
            ApartmentId = apartmentId;
            TenantId = tenantId;
            Dates = dates;

            ExpiresAt = DateTime.UtcNow.AddMinutes(15);
        }

        public bool IsExpired()
        {
            return DateTime.UtcNow > ExpiresAt;
        }
    }
}
