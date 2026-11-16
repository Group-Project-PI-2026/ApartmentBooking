using ApartmentBookingApplication.Apartments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentBooking.Controllers
{
    /// <summary>Catalog and search (public).</summary>
    [ApiController]
    [Route("api/apartments")]
    [AllowAnonymous]
    public class ApartmentsController : ControllerBase
    {
        private readonly ApartmentService _apartments;

        public ApartmentsController(ApartmentService apartments)
        {
            _apartments = apartments;
        }

        /// <summary>Search apartments; those intersecting bookings / active locks for the dates are excluded.</summary>
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ApartmentCardDto>>> Search(
            [FromQuery] int? districtId,
            [FromQuery] DateOnly? checkIn,
            [FromQuery] DateOnly? checkOut,
            [FromQuery] int? guestsCount,
            CancellationToken ct)
        {
            return Ok(await _apartments.SearchAsync(new ApartmentSearchQuery(districtId, checkIn, checkOut, guestsCount), ct));
        }

        /// <summary>Districts of Kyiv for the filter dropdown.</summary>
        [HttpGet("districts")]
        public async Task<ActionResult<IReadOnlyList<DistrictDto>>> GetDistricts(CancellationToken ct)
        {
            return Ok(await _apartments.GetDistrictsAsync(ct));
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<ApartmentDetailsDto>> GetById(Guid id, CancellationToken ct)
        {
            return Ok(await _apartments.GetByIdAsync(id, ct));
        }
    }
}
