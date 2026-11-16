namespace ApartmentBookingDomain.Entities
{
    /// <summary>District of Kyiv (reference data, seeded by migration).</summary>
    public class District
    {
        public int Id { get; private set; }
        public string Name { get; private set; }

        private District()
        {
            Name = string.Empty;
        }

        public District(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
