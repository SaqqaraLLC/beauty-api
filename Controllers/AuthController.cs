
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/auth")] 
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly IConfiguration _cfg;

    public AuthController(UserManager<ApplicationUser> userMgr, IConfiguration cfg)
    { _userMgr = userMgr; _cfg = cfg; }

    public record TokenRequest(string Email, string Password);

    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<IActionResult> Token([FromBody] TokenRequest req)
    {
        var user = await _userMgr.FindByEmailAsync(req.Email);
        if (user == null)
            return Unauthorized();

        var valid = await _userMgr.CheckPasswordAsync(user, req.Password);
        if (!valid)
            return Unauthorized();

        var roles = await _userMgr.GetRolesAsync(user);

        var key = _cfg["Jwt:Key"];
        var issuer = _cfg["Jwt:Issuer"];
        var audience = _cfg["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(key) ||
            string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience))
        {
            return StatusCode(500, new { error = "JWT configuration missing" });
        }

        var claims = new List<Claim>
{
    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
    new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, user.Email ?? string.Empty)
};

        claims.AddRange(
            roles.Select(r => new Claim(ClaimTypes.Role, r))
        );

        var creds = new SigningCredentials(
     new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
     SecurityAlgorithms.HmacSha256
 );

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        var jwt =
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .WriteToken(token);

        return Ok(new
        {
            access_token = jwt,
            roles
        });
    }

    public record ForgotDto(string Email);

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> Forgot([FromServices] EmailTemplateService emailSvc, [FromBody] ForgotDto dto)
    {
        var user = await _userMgr.FindByEmailAsync(dto.Email);
        if (user == null) return Ok();
        var token = await _userMgr.GeneratePasswordResetTokenAsync(user);
        var resetUrl = ($"{_cfg["Brand:PrimaryResetUrl"] ?? "https://app.saqqarallc.com/reset"}?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}");
        await emailSvc.SendResetAsync(user.Email!, user.Email!, resetUrl);
        return Ok();
    }

    public record ResetDto(string Email, string Token, string NewPassword);

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> Reset([FromBody] ResetDto dto)
    {
        var user = await _userMgr.FindByEmailAsync(dto.Email);
        if (user == null) return BadRequest();
        var res = await _userMgr.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
        if (!res.Succeeded) return BadRequest(res.Errors);
        return Ok();
    }
}
