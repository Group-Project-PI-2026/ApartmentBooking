namespace ApartmentBookingApplication.Options
{
    public class BookingOptions
    {
        public const string SectionName = "Booking";

        /// <summary>Prepayment taken on reservation, % of total price. 100 = always pay in full.</summary>
        public decimal DepositPercent { get; set; } = 30;

        /// <summary>Cancellation at least this many days before check-in is free.</summary>
        public int FreeCancellationDays { get; set; } = 7;

        /// <summary>Penalty for later cancellation, % of total price (capped by the paid amount).</summary>
        public decimal LateCancellationPenaltyPercent { get; set; } = 50;

        /// <summary>ISO currency code used for Stripe.</summary>
        public string Currency { get; set; } = "uah";
    }

    public class FrontendOptions
    {
        public const string SectionName = "Frontend";

        /// <summary>Base URL of the React SPA, used for Stripe success/cancel redirects.</summary>
        public string BaseUrl { get; set; } = "http://localhost:5173";
    }
}
