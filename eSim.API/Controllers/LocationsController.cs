using eSim.API.DTOs;
using eSim.Core.Constants;
using eSim.Infrastructure.Data;
using eSim.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eSim.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LocationsController : Controller
{
    public AppDbContext _dbContext { get; }
    public DistributorManager _client { get; }
    public ILogger<LocationsController> _logger { get; }
    public LocationsController(AppDbContext db, DistributorManager Client, ILogger<LocationsController> log)
    {
        _dbContext = db;
        _client = Client;
        _logger = log;
    }

    private record FinalResponse(List<LocationsDTO> mostPopular, List<LocationsDTO> multiRegion, List<LocationsDTO> singleRegion, List<LocationsDTO> allLocations);

    [HttpGet("get")]
    public async Task<IActionResult> GetLocations(CancellationToken ct)
    {
        try
        {
            var locations = await _dbContext.Locations.ToListAsync(ct);

            if (locations == null || locations.Count == 0)
            {
                return NotFound("No locations found.");
            }

            var allLocationsRes = await _client.GetAllLocations(ct);
            var mostPopular = locations.Where(l => l.Type == (int)LocationType.MostPopular).OrderBy(l => l.Ordering).Select(x => new LocationsDTO { Code = x.Code, Name = x.Name }).ToList();
            var multiRegion = allLocationsRes.Locations.Where(x => x.type == 2).Take(15).Select(y => new LocationsDTO
            {
                Code = y.code,
                Name = y.name,
                SubLocations = [.. y.SubLocations.Select(sl => new Coverages { code = sl.code, name = sl.name })],
                Type = 2
            }).ToList();
            var singleRegion = allLocationsRes.Locations.Where(x => x.type == 1).Take(40).Select(y => new LocationsDTO
            {
                Code = y.code,
                Name = y.name,
                SubLocations = [.. y.SubLocations.Select(sl => new Coverages { code = sl.code, name = sl.name })],
                Type = 1
            }).ToList();

            return Ok(new FinalResponse(mostPopular, multiRegion, singleRegion, [.. allLocationsRes.Locations.Select(x=> new LocationsDTO
            {
                Code = x.code,
                Name = x.name,
                Type = x.type,
                SubLocations = [.. x.SubLocations.Select(sl => new Coverages { code = sl.code, name = sl.name })]
            })]));
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to get locations with error: {ex.Message}");
        }
    }
}
