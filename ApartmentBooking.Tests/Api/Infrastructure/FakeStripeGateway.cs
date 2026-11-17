using System.Collections.Concurrent;
using System.Text.Json;
using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Exceptions;

namespace ApartmentBooking.Tests.Api.Infrastructure;

/// <summary>
/// In-process replacement of Stripe used by API/E2E tests.
/// Webhook payloads are plain JSON <see cref="PaymentWebhookEvent"/>; the signature is valid only when it equals <see cref="ValidSignature"/>.
/// </summary>
internal sealed class FakeStripeGateway : IPaymentGateway
{
    public const string ValidSignature = "t=1,v1=valid-test-signature";

    public ConcurrentQueue<CreatedSession> Sessions { get; } = new();
    public ConcurrentQueue<RefundCall> Refunds { get; } = new();

    /// <summary>When true, the next CreateCheckoutSession call fails (simulates Stripe outage).</summary>
    public bool FailNextCheckout { get; set; }

    /// <summary>When true, refunds fail.</summary>
    public bool FailRefunds { get; set; }

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (FailNextCheckout)
        {
            FailNextCheckout = false;
            throw new PaymentGatewayException("Stripe is unavailable (simulated).");
        }

        var sessionId = $"cs_test_{Guid.NewGuid():N}";
        Sessions.Enqueue(new CreatedSession(sessionId, request));
        return Task.FromResult(new CheckoutSessionResult(sessionId, $"https://checkout.stripe.test/{sessionId}"));
    }

    public Task RefundAsync(string paymentIntentId, decimal amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (FailRefunds)
            throw new PaymentGatewayException("Refund failed (simulated).");

        Refunds.Enqueue(new RefundCall(paymentIntentId, amount, idempotencyKey));
        return Task.CompletedTask;
    }

    public PaymentWebhookEvent ParseWebhookEvent(string payload, string? signature)
    {
        if (signature != ValidSignature)
            throw new InvalidWebhookSignatureException("Invalid Stripe signature.");

        return JsonSerializer.Deserialize<PaymentWebhookEvent>(payload, ApiJson.Options)
               ?? throw new InvalidWebhookSignatureException("Empty payload.");
    }

    public CreatedSession SessionFor(string metadataKey, string metadataValue) =>
        Sessions.Last(s => s.Request.Metadata.TryGetValue(metadataKey, out var v) && v == metadataValue);

    internal sealed record CreatedSession(string SessionId, CheckoutSessionRequest Request);

    internal sealed record RefundCall(string PaymentIntentId, decimal Amount, string IdempotencyKey);
}
