using eSim.API.DTOs;
using eSim.Core.Interfaces;
using eSim.Core.Models;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using static QRCoder.PayloadGenerator;

namespace eSim.API.Controllers;


[ApiController]
[Route("api")]
public class SupportController : Controller
{
    private readonly IEmailService _email;

    public SupportController(IEmailService email) => _email = email;

    [HttpPost("send-contact")]
    public async Task<IActionResult> SendContactAsync([FromBody] ContactRequest req, CancellationToken ct)
    {
        // 1) Honeypot
        if (!string.IsNullOrWhiteSpace(req.company))
            return BadRequest(new { error = "Failed to send email" });

        // 2) Validate required fields
        if (string.IsNullOrWhiteSpace(req.name) ||
            string.IsNullOrWhiteSpace(req.email) ||
            string.IsNullOrWhiteSpace(req.topic) ||
            string.IsNullOrWhiteSpace(req.message))
        {
            return BadRequest(new { error = "Please fill in all required fields." });
        }

        if (!new EmailAddressAttribute().IsValid(req.email))
            return BadRequest(new { error = "Invalid email" });

        try
        {
            await _email.SendSupportContactEmailAsync(req, ct);
            return Ok(new { success = true, message = "Email sent successfully" });
        }
        catch
        {
            return StatusCode(500, new { error = "Failed to send email" });
        }
    }
}
