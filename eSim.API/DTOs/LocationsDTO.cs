namespace eSim.API.DTOs
{
    public class LocationsDTO
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
        public int Type { get; set; }
        public IReadOnlyList<Coverages>? SubLocations { get; set; }
    }

    public class Coverages
    {
        public string? name { get; set; }
        public string? code { get; set; }
    }
}
