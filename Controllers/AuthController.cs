using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Beauty.Api.Contracts.Auth;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _config;
    
    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration config)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
    }

    // ✅ LOGIN + LOCKOUT
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto req)
    {
        var result = await _signInManager.PasswordSignInAsync(
            req.Email,
            req.Password,
            isPersistent: true,
            lockoutOnFailure: true
        );

        if (result.IsLockedOut)
            return Unauthorized(new { code = "LOCKED_OUT" });

        if (result.RequiresTwoFactor)
            return Ok(new { requiresMfa = true });

        if (!result.Succeeded)
            return Unauthorized(new { code = "INVALID_CREDENTIALS" });

        return Ok();
    }


    // ✅ MFA VERIFY
    [HttpPost("mfa/verify")]
    public async Task<IActionResult> VerifyMfa([FromBody] MfaVerifyRequest req)
    {
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return Unauthorized();

        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            req.Code
        );

        if (!valid) return Unauthorized("Invalid code");

        await _signInManager.SignInAsync(user, true);
        return Ok();
    }

    // ✅ MFA SETUP
    [Authorize]
    [HttpPost("mfa/setup")]
    public async Task<IActionResult> SetupMfa()
    {
        var user = await _userManager.GetUserAsync(User);

        
var key = await _userManager.GetAuthenticatorKeyAsync(user);

if (string.IsNullOrEmpty(key))
{
    await _userManager.ResetAuthenticatorKeyAsync(user);
    key = await _userManager.GetAuthenticatorKeyAsync(user);
}


        return Ok(new
        {
            sharedKey = key,
            qrCodeUri = GenerateQrCodeUri(user.Email!, key)
        });
    }

    // ✅ JWT TOKEN (for Swagger / API clients)
    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<IActionResult> Token([FromBody] TokenRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.Name, user.Email!)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));


        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:SigningKey"]!)
        );

        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials
        );

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new
        {
            access_token = jwt
        }


        ); 
    }

        // ✅ PASSWORD RESET
    [HttpPost("forgot-password")]
    [AllowAnonymous]

    public async Task<IActionResult> ForgotPassword(
          [FromServices] EmailTemplateService emailSvc,
          [FromBody] ForgotDto dto)
    {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null) return Ok();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = $"{_config["Brand:PrimaryResetUrl"]}?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";

            await emailSvc.SendResetAsync(user.Email!, user.Email!, resetUrl);
            return Ok();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetDto dto)
    {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null) return BadRequest();

            var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
            if (!result.Succeeded) return BadRequest(result.Errors);

            return Ok();
        
    }

    // ✅ Helpers
    private static string GenerateQrCodeUri(string email, string key)
        => $"otpauth://totp/Saqqara:{email}?secret={key}&issuer=Saqqara";
}