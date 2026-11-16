using System.Collections.Concurrent;
using ApartmentBookingApplication.Abstractions;

namespace ApartmentBooking.Tests.TestDoubles;

internal sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Messages { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        Messages.Enqueue(new EmailMessage(to, subject, body));
        return Task.CompletedTask;
    }

    internal sealed record EmailMessage(string To, string Subject, string Body);
}
