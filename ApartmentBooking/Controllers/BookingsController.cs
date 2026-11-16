using ApartmentBooking.Auth;
using ApartmentBookingApplication.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentBooking.Controllers
{
    /// <summary>Tenant's personal cabinet.</summary>
    [ApiController]
    [Route("api/bookings")]
    [Authorize(Policy = Policies.Tenant)]
    public class BookingsController : ControllerBase
    {
        private readonly BookingService _bookings;

        public BookingsController(BookingService bookings)
        {
            _bookings = bookings;
        }

        /// <summary>Creates a 15-minute booking_lock and returns the Stripe Checkout URL.</summary>
        [HttpPost("reserve")]
        public async Task<ActionResult<ReserveBookingResponse>> Reserve([FromBody] ReserveBookingRequest request, CancellationToken ct)
        {
            return Ok(await _bookings.ReserveAsync(request, ct));
        }

        /// <summary>Bookings of the current user. status = Future | Past | Cancelled (all when omitted).</summary>
        [HttpGet("my")]
        public async Task<ActionResult<IReadOnlyList<BookingListItemDto>>> GetMy([FromQuery] BookingTab? status, CancellationToken ct)
        {
            return Ok(await _bookings.GetMyAsync(status, ct));
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<BookingDetailsDto>> GetById(Guid id, CancellationToken ct)
        {
            return Ok(await _bookings.GetByIdAsync(id, ct));
        }

        /// <summary>Creates a Stripe Checkout Session for the remaining balance.</summary>
        [HttpPost("{id:guid}/pay-balance")]
        public async Task<ActionResult<CheckoutResponse>> PayBalance(Guid id, CancellationToken ct)
        {
            return Ok(await _bookings.PayBalanceAsync(id, ct));
        }

        [HttpGet("{id:guid}/refund-preview")]
        public async Task<ActionResult<RefundPreviewDto>> RefundPreview(Guid id, CancellationToken ct)
        {
            return Ok(await _bookings.GetRefundPreviewAsync(id, ct));
        }

        /// <summary>Cancels the booking, releases the dates and refunds through Stripe.</summary>
        [HttpPost("{id:guid}/cancel")]
        public async Task<ActionResult<CancelBookingResponse>> Cancel(Guid id, CancellationToken ct)
        {
            return Ok(await _bookings.CancelAsync(id, ct));
        }
    }
}
