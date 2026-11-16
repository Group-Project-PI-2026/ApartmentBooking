namespace ApartmentBookingDomain.Enums
{
    public enum BookingStatus
    {
        /// <summary>Only the prepayment (deposit) is paid, the balance is still due.</summary>
        PartiallyPaid,
        Paid,
        Cancelled,
        Completed
    }
}
