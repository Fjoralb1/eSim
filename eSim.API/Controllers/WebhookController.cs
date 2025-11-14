using eSim.Core.Contracts.WebHooks;
using eSim.Core.Interfaces;
using eSim.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eSim.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : Controller
{
    public DistributorManager _client { get; }
    public IPayPalClient _paypalClient { get; }

    public WebhookController(
        DistributorManager client, IPayPalClient payPalClient)
    {
        _client = client;
        _paypalClient = payPalClient;
    }

    [AllowAnonymous]
    [HttpPost("esimWebhook")]
    [Consumes("application/json")]
    public async Task<IActionResult> EsimWebhook([FromBody] EsimAccessWebhookEnvelope envelope, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _client.HandleEsimAccessWebhookAsync(envelope.notifyType, envelope.content, ct);

        return Ok(new { ok = true });
    }

    [AllowAnonymous]
    [HttpPost("ManetWebhook")]
    [Consumes("application/json")]
    public async Task<IActionResult> ManetWebhook([FromBody] ManetWebhookEnvelope envelope, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _client.HandleManetWebhookAsync(envelope, ct);

        return Ok(new { ok = true });
    }
}