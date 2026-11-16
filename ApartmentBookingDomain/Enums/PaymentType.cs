namespace ApartmentBookingDomain.Enums
{
    public enum PaymentType
    {
        /// <summary>First payment made when the reservation is converted into a booking.</summary>
        Reservation,
        /// <summary>Additional payment of the remaining balance.</summary>
        Balance
    }
}
