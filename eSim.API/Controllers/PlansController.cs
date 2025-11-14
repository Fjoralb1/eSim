using eSim.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eSim.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlansController : ControllerBase
{
    private readonly DistributorManager _client;
    private readonly ILogger<PlansController> _log;

    public PlansController(
        DistributorManager client,
        ILogger<PlansController> log)
    {
        _client = client;
        _log = log;
    }

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string locationCode, CancellationToken ct)
    {
        try
        {
            _log.LogInformation("Building plans list...");

            var packages = await _client.GetPackagesAsync(locationCode, ct);

            _log.LogInformation("Plans list built.");

            return Ok(packages);
        }
        catch
        {
            return StatusCode(500, "Failed to get plans.");
        }
    }
}
