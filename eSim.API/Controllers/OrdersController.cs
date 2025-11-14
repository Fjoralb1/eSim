using eSim.API.DTOs;
using eSim.Core.Contracts;
using eSim.Core.Interfaces;
using eSim.Infrastructure.Data;
using eSim.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace eSim.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly DistributorManager _client;
    private readonly IOrderWorkflow _flow;
    private readonly UserManager<ApplicationUser> _users;

    public OrdersController(
        DistributorManager client,
        IOrderWorkflow orderWorkflow,
        UserManager<ApplicationUser> users)
    {
        _client = client;
        _flow = orderWorkflow;
        _users = users;
    }

    [HttpGet("GetOrderForUser")]
    public async Task<IActionResult> GetOrderForUser(CancellationToken ct)
    {
        var userid = _users.GetUserId(User)!;
        var result = await _client.GetOrdersForUserAsync(userid, ct);

        var finalResult = result.Orders.Select(x => new OrderDTO
        {
            OrderNo = x.DistributorOrderNo!,
            PackageName = x.PackageName!,
            Amount = x.RPrice * x.Count * (x.PeriodNum != 0 ? x.PeriodNum : 1),
            CreatedAt = x.CreatedAt
        });

        return Ok(finalResult);
    }

    [HttpGet("GetOrderItemsForOrder")]
    public async Task<IActionResult> GetOrderItemsForOrder([FromQuery] string orderNo, CancellationToken ct)
    {
        var userId = _users.GetUserId(User)!;

        var result = await _client.GetOrderItemsAsync(orderNo, userId, ct);

        return Ok(result);
    }

    [HttpGet("GetInvoiceForOrder")]
    public async Task<IActionResult> GetInvoiceForOrder([FromQuery] string orderNo, CancellationToken ct)
    {
        try
        {
            var loggedInUser = await _users.GetUserAsync(User);

            var result = await _client.GetInvoiceForOrder(orderNo, loggedInUser, ct);

            Response.Headers.ContentDisposition = $"inline; filename=invoice_{orderNo}.pdf";
            return File(result!, "application/pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] OrderRequestDTO req, CancellationToken ct)
    {
        var user = await _users.GetUserAsync(User);

        if (user == null)
        {
            return Unauthorized();
        }

        var paypalOrderId = await _flow.StartCheckoutAsync(new OrderPackageRequest(req.PackageCode, req.PackageName, req.Count,
            req.Price, req.RetailPrice, req.PeriodNum, req.Provider, req.LocationCode, user.CountryOfResidence), user!.Id, ct);

        return Ok(paypalOrderId);
    }

    [HttpPost("capture")]
    public async Task<IActionResult> Capture([FromQuery] string orderId, CancellationToken ct)
    {
        await _flow.CaptureAndPlaceOrderAsync(orderId, ct);
        return Ok(new { ok = true });
    }
}