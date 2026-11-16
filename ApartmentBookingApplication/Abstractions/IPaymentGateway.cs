namespace ApartmentBookingApplication.Abstractions
{
    public interface IPaymentGateway
    {
        /// <summary>Creates a hosted checkout page (Stripe Checkout Session).</summary>
        Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken = default);

        /// <summary>Refunds (part of) a payment. The idempotency key protects against double refunds on retries.</summary>
        Task RefundAsync(string paymentIntentId, decimal amount, string idempotencyKey, CancellationToken cancellationToken = default);

        /// <summary>Verifies the webhook signature and parses the event.</summary>
        /// <exception cref="Exceptions.InvalidWebhookSignatureException">Signature is invalid.</exception>
        PaymentWebhookEvent ParseWebhookEvent(string payload, string? signature);
    }

    public record CheckoutSessionRequest(
        string ProductName,
        decimal Amount,
        string? CustomerEmail,
        string SuccessUrl,
        string CancelUrl,
        IReadOnlyDictionary<string, string> Metadata);

    public record CheckoutSessionResult(string SessionId, string Url);

    public record PaymentWebhookEvent(
        string EventId,
        string Type,
        string? SessionId,
        string? PaymentIntentId,
        string? PaymentStatus,
        decimal Amount,
        string? CustomerEmail,
        IReadOnlyDictionary<string, string> Metadata);
}
