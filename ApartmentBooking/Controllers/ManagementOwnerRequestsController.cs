using ApartmentBooking.Auth;
using ApartmentBookingApplication.Common;
using ApartmentBookingApplication.OwnerRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentBooking.Controllers
{
    /// <summary>Processing of owners' cooperation requests (Manager / Admin).</summary>
    [ApiController]
    [Route("api/management/owner-requests")]
    [Authorize(Policy = Policies.Manager)]
    public class ManagementOwnerRequestsController : ControllerBase
    {
        private readonly OwnerRequestService _requests;

        public ManagementOwnerRequestsController(OwnerRequestService requests)
        {
            _requests = requests;
        }

        /// <summary>Paged table. search = number / name / address / phone / email; status = New | Approved | NeedsClarification | Rejected.</summary>
        [HttpGet]
        public async Task<ActionResult<PagedResult<OwnerRequestListItemDto>>> GetList([FromQuery] OwnerRequestListQuery query, CancellationToken ct)
        {
            return Ok(await _requests.GetPagedAsync(query, ct));
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<OwnerRequestDetailsDto>> GetById(Guid id, CancellationToken ct)
        {
            return Ok(await _requests.GetByIdAsync(id, ct));
        }

        /// <summary>Approves the request and creates the Apartment in the catalog.</summary>
        [HttpPost("{id:guid}/approve")]
        public async Task<ActionResult<OwnerRequestDetailsDto>> Approve(Guid id, [FromBody] ApproveOwnerRequestRequest? request, CancellationToken ct)
        {
            return Ok(await _requests.ApproveAsync(id, request ?? new ApproveOwnerRequestRequest(), ct));
        }

        [HttpPost("{id:guid}/reject")]
        public async Task<ActionResult<OwnerRequestDetailsDto>> Reject(Guid id, [FromBody] OwnerRequestMessageRequest request, CancellationToken ct)
        {
            return Ok(await _requests.RejectAsync(id, request.Message, ct));
        }

        /// <summary>Asks the owner for more details; the message is emailed to them.</summary>
        [HttpPost("{id:guid}/clarify")]
        public async Task<ActionResult<OwnerRequestDetailsDto>> Clarify(Guid id, [FromBody] OwnerRequestMessageRequest request, CancellationToken ct)
        {
            return Ok(await _requests.RequestClarificationAsync(id, request.Message, ct));
        }
    }
}
