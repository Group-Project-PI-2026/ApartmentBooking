using ApartmentBookingApplication.Abstractions;

namespace ApartmentBooking.Auth
{
    public class HttpCurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpCurrentUser(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

        public Guid UserId
        {
            get
            {
                var sub = _accessor.HttpContext?.User.FindFirst("sub")?.Value;
                if (!Guid.TryParse(sub, out var id))
                    throw new UnauthorizedAccessException("Token does not contain a valid 'sub' claim.");
                return id;
            }
        }

        public string? Email => _accessor.HttpContext?.User.FindFirst("email")?.Value;

        public bool IsInRole(string role) => _accessor.HttpContext?.User.IsInRole(role) == true;
    }
}
