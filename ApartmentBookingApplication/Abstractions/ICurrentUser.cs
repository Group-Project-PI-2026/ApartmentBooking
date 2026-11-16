namespace ApartmentBookingApplication.Abstractions
{
    /// <summary>The authenticated user of the current request (taken from the Keycloak JWT).</summary>
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }

        /// <summary>Keycloak user id (<c>sub</c> claim). Throws if the request is anonymous.</summary>
        Guid UserId { get; }

        string? Email { get; }

        bool IsInRole(string role);
    }
}
