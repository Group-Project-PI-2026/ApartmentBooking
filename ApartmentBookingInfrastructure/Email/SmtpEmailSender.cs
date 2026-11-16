using System.Net;
using System.Net.Mail;
using ApartmentBookingApplication.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentBookingInfrastructure.Email
{
    public class SmtpOptions
    {
        public const string SectionName = "Smtp";

        /// <summary>When empty, emails are only written to the log (useful for local development).</summary>
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string From { get; set; } = "no-reply@apartment-booking.local";
    }

    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _options;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.Host))
            {
                _logger.LogInformation("SMTP is not configured. Email to {To}: {Subject}\n{Body}", to, subject, body);
                return;
            }

            try
            {
                using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl };
                if (!string.IsNullOrEmpty(_options.UserName))
                    client.Credentials = new NetworkCredential(_options.UserName, _options.Password);

                using var message = new MailMessage(_options.From, to, subject, body);
                await client.SendMailAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                // Notification failures must never break the business operation.
                _logger.LogError(ex, "Failed to send email '{Subject}' to {To}", subject, to);
            }
        }
    }
}
