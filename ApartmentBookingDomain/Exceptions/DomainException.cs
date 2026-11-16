namespace ApartmentBookingDomain.Exceptions
{
    /// <summary>
    /// Thrown when a business invariant of a domain entity is violated.
    /// Mapped to HTTP 400 by the API layer.
    /// </summary>
    public class DomainException : Exception
    {
        public DomainException(string message) : base(message) { }
    }
}
