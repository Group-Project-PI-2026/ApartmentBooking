using ApartmentBookingApplication.Abstractions;

namespace ApartmentBooking.Tests.TestDoubles;

internal sealed class TestCurrentUser(Guid userId, string? email = null, params string[] roles) : ICurrentUser
{
    private readonly HashSet<string> _roles = new(roles, StringComparer.OrdinalIgnoreCase);

    public bool IsAuthenticated => true;
    public Guid UserId { get; } = userId;
    public string? Email { get; } = email;

    public bool IsInRole(string role) => _roles.Contains(role);
}
