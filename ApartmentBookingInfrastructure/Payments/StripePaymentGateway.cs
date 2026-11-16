using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Exceptions;
using ApartmentBookingApplication.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace ApartmentBookingInfrastructure.Payments
{
    public class StripeOptions
    {
        public const string SectionName = "Stripe";

        /// <summary>sk_test_... / sk_live_... (keep in user-secrets / environment variables).</summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>whsec_... signing secret of the webhook endpoint.</summary>
        public string WebhookSecret { get; set; } = string.Empty;
    }

    public class StripePaymentGateway : IPaymentGateway
    {
        // Stripe requires a Checkout Session to live at least 30 minutes. The 15-minute booking lock is shorter,
        // so a late payment is handled in the webhook (booking is created if dates are still free, refunded otherwise).
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(31);

        private readonly StripeClient _client;
        private readonly StripeOptions _stripeOptions;
        private readonly BookingOptions _bookingOptions;
        private readonly ILogger<StripePaymentGateway> _logger;

        public StripePaymentGateway(
            IOptions<StripeOptions> stripeOptions,
            IOptions<BookingOptions> bookingOptions,
            ILogger<StripePaymentGateway> logger)
        {
            _stripeOptions = stripeOptions.Value;
            _bookingOptions = bookingOptions.Value;
            _logger = logger;
            _client = new StripeClient(string.IsNullOrEmpty(_stripeOptions.SecretKey) ? "sk_not_configured" : _stripeOptions.SecretKey);
        }

        public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken = default)
        {
            var metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value);

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                CustomerEmail = request.CustomerEmail,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                ExpiresAt = DateTime.UtcNow.Add(SessionLifetime),
                Metadata = metadata,
                PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata },
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = _bookingOptions.Currency,
                            UnitAmount = ToMinorUnits(request.Amount),
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = request.ProductName }
                        }
                    }
                }
            };

            try
            {
                var session = await new SessionService(_client).CreateAsync(options, cancellationToken: cancellationToken);
                return new CheckoutSessionResult(session.Id, session.Url);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe checkout session creation failed");
                throw new PaymentGatewayException("Payment provider is unavailable. Please try again later.", ex);
            }
        }

        public async Task RefundAsync(string paymentIntentId, decimal amount, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            try
            {
                await new RefundService(_client).CreateAsync(
                    new RefundCreateOptions { PaymentIntent = paymentIntentId, Amount = ToMinorUnits(amount) },
                    new RequestOptions { IdempotencyKey = idempotencyKey },
                    cancellationToken);
            }
            catch (StripeException ex)
            {
                throw new PaymentGatewayException($"Refund for {paymentIntentId} failed: {ex.Message}", ex);
            }
        }

        public PaymentWebhookEvent ParseWebhookEvent(string payload, string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                throw new InvalidWebhookSignatureException("Missing Stripe-Signature header.");

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(payload, signature, _stripeOptions.WebhookSecret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                throw new InvalidWebhookSignatureException("Invalid Stripe webhook signature.", ex);
            }

            if (stripeEvent.Data.Object is Session session)
            {
                return new PaymentWebhookEvent(
                    stripeEvent.Id,
                    stripeEvent.Type,
                    session.Id,
                    session.PaymentIntentId,
                    session.PaymentStatus,
                    FromMinorUnits(session.AmountTotal ?? 0),
                    session.CustomerDetails?.Email ?? session.CustomerEmail,
                    session.Metadata ?? new Dictionary<string, string>());
            }

            return new PaymentWebhookEvent(
                stripeEvent.Id, stripeEvent.Type, null, null, null, 0, null, new Dictionary<string, string>());
        }

        private static long ToMinorUnits(decimal amount) =>
            (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

        private static decimal FromMinorUnits(long amount) => amount / 100m;
    }
}
