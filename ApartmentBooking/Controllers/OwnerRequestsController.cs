using ApartmentBookingApplication.OwnerRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentBooking.Controllers
{
    /// <summary>
    /// Owner side of cooperation requests. Any signed-in user may apply
    /// (the "owner" role is typically granted after the first approved request).
    /// </summary>
    [ApiController]
    [Route("api/owner-requests")]
    [Authorize]
    public class OwnerRequestsController : ControllerBase
    {
        private readonly OwnerRequestService _requests;

        public OwnerRequestsController(OwnerRequestService requests)
        {
            _requests = requests;
        }

        [HttpPost]
        public async Task<ActionResult<OwnerRequestDetailsDto>> Create([FromBody] CreateOwnerRequestRequest request, CancellationToken ct)
        {
            var created = await _requests.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetMyById), new { id = created.Id }, created);
        }

        [HttpGet("my")]
        public async Task<ActionResult<IReadOnlyList<OwnerRequestListItemDto>>> GetMy(CancellationToken ct)
        {
            return Ok(await _requests.GetMyAsync(ct));
        }

        [HttpGet("my/{id:guid}")]
        public async Task<ActionResult<OwnerRequestDetailsDto>> GetMyById(Guid id, CancellationToken ct)
        {
            return Ok(await _requests.GetMyByIdAsync(id, ct));
        }
    }
}
