using ApartmentBookingApplication.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentBooking.Controllers
{
    /// <summary>System webhooks: no JWT, authenticity is verified by the signature.</summary>
    [ApiController]
    [Route("api/webhooks")]
    [AllowAnonymous]
    public class WebhooksController : ControllerBase
    {
        private readonly PaymentWebhookService _webhooks;

        public WebhooksController(PaymentWebhookService webhooks)
        {
            _webhooks = webhooks;
        }

        /// <summary>
        /// Stripe events (checkout.session.completed). Returns 200 when processed or ignored,
        /// 400 on a bad signature and 5xx on failures so Stripe retries the delivery.
        /// </summary>
        [HttpPost("stripe")]
        public async Task<IActionResult> Stripe(CancellationToken ct)
        {
            // The signature is computed over the raw body, so it must be read as-is.
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync(ct);

            await _webhooks.HandleAsync(payload, Request.Headers["Stripe-Signature"], ct);
            return Ok();
        }
    }
}
