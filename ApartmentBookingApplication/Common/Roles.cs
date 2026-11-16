namespace ApartmentBookingApplication.Common
{
    /// <summary>Realm roles configured in Keycloak.</summary>
    public static class Roles
    {
        public const string Tenant = "tenant";
        public const string Owner = "owner";
        public const string Manager = "manager";
        public const string Admin = "admin";
    }

    public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
}
