using eSim.Core.Constants;
using eSim.Core.Interfaces;
using eSim.Infrastructure.Data;
using eSim.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace eSim.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;
    private readonly IEmailService _email;

    public AuthController(UserManager<ApplicationUser> userManager, IEmailService emailService, IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
        _email = emailService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
            return BadRequest("Email already registered.");

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            FullName = dto.FirstName + " " + dto.LastName,
            CountryOfResidence = dto.CountryOfResidence
        };

        var result = await _userManager.CreateAsync(user, dto.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        await _userManager.AddToRoleAsync(user, RoleNames.Customer);

        // Generate email confirmation token
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Create confirmation link
        var frontendUrl = _config["App:PublicBaseUrl"];
        var confirmationLink = $"{frontendUrl}/verify-email?userId={user.Id}&token={WebUtility.UrlEncode(token)}";

        // Send confirmation email
        await _email.SendEmailConfirmationAsync(user.Email, user.UserName, confirmationLink!, ct);

        return Ok(new { message = "User registered successfully. Please check your email to confirm your account." });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] EmailConfirmationRequest request)
    {
        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.Token))
            {
                return BadRequest(new { status = "error", reason = "invalid", message = "Invalid parameters." });
            }

            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { status = "error", reason = "user_not_found", message = "User not found." });
            }

            // Check if already verified
            if (user.EmailConfirmed)
            {
                return BadRequest(new { status = "error", reason = "already_verified", message = "Email has already been verified." });
            }

            // Verify token
            var result = await _userManager.ConfirmEmailAsync(user, request.Token);
            if (!result.Succeeded)
            {
                // Token expired or invalid
                var errorMessage = result.Errors.FirstOrDefault()?.Description ?? "Invalid or expired token.";
                return BadRequest(new { status = "error", reason = "expired", message = errorMessage });
            }

            // ✅ SUCCESS
            return Ok(new { status = "success", message = "Email confirmed successfully." });
        }
        catch
        {
            return StatusCode(500, new { status = "error", reason = "server_error", message = "An error occurred during verification." });
        }
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] EmailOnlyRequest request, CancellationToken ct)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            // Don't reveal if email exists or not (security)
            if (user == null)
            {
                return Ok(new { message = "If the email exists, a verification link has been sent." });
            }

            // If already verified
            if (user.EmailConfirmed)
            {
                return BadRequest(new { message = "Email already verified" });
            }

            // Generate new token
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            var verificationUrl = Url.Action(
                action: nameof(ConfirmEmail),
                controller: "auth",
                values: new { userId = user.Id, token = WebUtility.UrlEncode(token) },
                protocol: Request.Scheme
            );

            await _email.SendEmailConfirmationAsync(user.Email!, user.UserName!, verificationUrl!, ct);

            return Ok(new { message = "Verification email sent successfully." });
        }
        catch
        {
            return StatusCode(500, new { message = "Failed to send verification email." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null || !await _userManager.CheckPasswordAsync(user, dto.Password))
            return Unauthorized();

        var token = await GenerateJwtTokenAsync(user);
        return Ok(new { token });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Unauthorized();

        // Optional: short-circuit if new == current
        if (await _userManager.CheckPasswordAsync(user, req.CurrentPassword) && req.CurrentPassword == req.NewPassword)
            return BadRequest(new { error = "New password must be different from the current password." });

        var result = await _userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new
            {
                errors = result.Errors.Select(e => new { e.Code, e.Description })
            });

        // IMPORTANT: issue a fresh JWT so the frontend can replace the stored token
        var newJwt = await GenerateJwtTokenAsync(user);

        return Ok(new
        {
            message = "Password changed successfully.",
            token = newJwt
        });
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] EmailOnlyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return Ok();

        var user = await _userManager.FindByEmailAsync(req.Email);

        if (user is null) return Ok();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var tokenSafe = WebEncoders.Base64UrlEncode(tokenBytes);

        var templateUrl = _config["App:PublicBaseUrl"] + _config["CompanyInformation:ResetPasswordUrl"];
        var resetUrl = templateUrl.Replace("{uid}", user.Id).Replace("{token}", tokenSafe);

        await _email.SendPasswordResetEmailAsync(
            toEmail: req.Email,
            userName: user.FullName,
            resetUrl: resetUrl,
            ct
        );

        return Ok();
    }


    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var user = await _userManager.FindByIdAsync(req.UserId);
        if (user is null)
            return BadRequest(new { error = "Invalid reset request." });

        // Decode token
        string token;
        try
        {
            var tokenBytes = WebEncoders.Base64UrlDecode(req.Token);
            token = Encoding.UTF8.GetString(tokenBytes);
        }
        catch
        {
            return BadRequest(new { error = "Invalid token." });
        }

        var result = await _userManager.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => new { e.Code, e.Description }) });

        return Ok(new { message = "Password has been reset successfully." });
    }

    public async Task<string> GenerateJwtTokenAsync(ApplicationUser user)
    {
        var jwt = _config.GetSection("JwtSettings");              // <- ensure this matches Program.cs
        var keyBytes = Encoding.UTF8.GetBytes(jwt["Secret"]!);    // strong key (>= 32 bytes recommended)
        var signingKey = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        // Base claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("fullname", user.FullName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var securityStamp = await _userManager.GetSecurityStampAsync(user);
        if (!string.IsNullOrEmpty(securityStamp))
            claims.Add(new Claim("security_stamp", securityStamp));

        var expires = DateTime.UtcNow.AddMinutes(double.Parse(jwt["ExpiresInMinutes"] ?? "60"));

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record RegisterDto(string Email, string Password, string FirstName, string LastName, string CountryOfResidence);
public record LoginDto(string Email, string Password);
public record EmailOnlyRequest(string Email);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record EmailConfirmationRequest(string UserId, string Token);

public sealed class ResetPasswordRequest
{
    public string UserId { get; set; } = null!;
    public string Token { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
}