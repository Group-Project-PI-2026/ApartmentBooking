namespace ApartmentBookingApplication.Abstractions
{
    public interface IEmailSender
    {
        /// <summary>Sends an email. Implementations must not throw — failures are logged.</summary>
        Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    }
}
