namespace ApartmentBookingDomain.Entities
{
    /// <summary>
    /// Temporary hold of dates while the tenant pays (table booking_locks).
    /// </summary>
    public class BookingLock
    {
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

        public Guid Id { get; private set; }
        public Guid ApartmentId { get; private set; }
        public Guid TenantId { get; private set; }
        public string? TenantEmail { get; private set; }
        public DateRange Dates { get; private set; } = null!;
        public decimal TotalPrice { get; private set; }
        public decimal AmountToPay { get; private set; }
        public string? CheckoutSessionId { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime ExpiresAt { get; private set; }

        private BookingLock() { }

        public BookingLock(Guid apartmentId, Guid tenantId, string? tenantEmail, DateRange dates, decimal totalPrice, decimal amountToPay)
        {
            Id = Guid.NewGuid();
            ApartmentId = apartmentId;
            TenantId = tenantId;
            TenantEmail = tenantEmail;
            Dates = dates;
            TotalPrice = totalPrice;
            AmountToPay = amountToPay;
            CreatedAt = DateTime.UtcNow;
            ExpiresAt = CreatedAt.Add(Duration);
        }

        public void AttachCheckoutSession(string sessionId)
        {
            CheckoutSessionId = sessionId;
        }

        public bool IsExpired()
        {
            return DateTime.UtcNow > ExpiresAt;
        }
    }
}
