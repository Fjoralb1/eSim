namespace eSim.Core.Contracts
{
    public record GetLocationsResponse(bool success, string? errMessage, IReadOnlyList<LocationData> Locations);
    public record LocationData(string code, string name, int type, IReadOnlyList<SubLocations> SubLocations);
    public record SubLocations(string code, string name);
}
