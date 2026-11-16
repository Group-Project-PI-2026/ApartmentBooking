using System.Collections.Concurrent;
using ApartmentBookingApplication.Abstractions;

namespace ApartmentBooking.Tests.TestDoubles;

internal sealed class RecordingPaymentGateway : IPaymentGateway
{
    private int _sessionNumber;

    public ConcurrentQueue<CheckoutSessionRequest> CheckoutRequests { get; } = new();
    public ConcurrentQueue<RefundCall> Refunds { get; } = new();
    public PaymentWebhookEvent? WebhookEvent { get; set; }
    public Exception? CheckoutException { get; set; }

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CheckoutSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CheckoutException is not null)
            throw CheckoutException;

        CheckoutRequests.Enqueue(request);
        var number = Interlocked.Increment(ref _sessionNumber);
        return Task.FromResult(new CheckoutSessionResult($"cs_test_{number}", $"https://checkout.example.test/{number}"));
    }

    public Task RefundAsync(
        string paymentIntentId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Refunds.Enqueue(new RefundCall(paymentIntentId, amount, idempotencyKey));
        return Task.CompletedTask;
    }

    public PaymentWebhookEvent ParseWebhookEvent(string payload, string? signature) =>
        WebhookEvent ?? throw new InvalidOperationException("Set WebhookEvent before invoking the webhook service.");

    internal sealed record RefundCall(string PaymentIntentId, decimal Amount, string IdempotencyKey);
}
