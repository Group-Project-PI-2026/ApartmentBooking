namespace ApartmentBookingApplication.Bookings
{
    /// <summary>Keys of the metadata attached to Stripe Checkout Sessions and read back in the webhook.</summary>
    internal static class PaymentMetadata
    {
        public const string Kind = "kind";
        public const string KindReservation = "reservation";
        public const string KindBalance = "balance";

        public const string LockId = "lock_id";
        public const string BookingId = "booking_id";
        public const string ApartmentId = "apartment_id";
        public const string TenantId = "tenant_id";
        public const string CheckIn = "check_in";
        public const string CheckOut = "check_out";
        public const string TotalPrice = "total_price";

        public const string DateFormat = "yyyy-MM-dd";
    }
}
