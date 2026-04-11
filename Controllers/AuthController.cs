using Beauty.Api.Authorization;
using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly BeautyDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration config,
        BeautyDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _db = db;
    }

    // ✅ LOGIN + LOCKOUT
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null)
            return Unauthorized(new { code = "INVALID_CREDENTIALS" });

        // Check lockout before verifying password (prevents timing oracle)
        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized(new { code = "LOCKED_OUT" });

        var result = await _signInManager.CheckPasswordSignInAsync(
            user,
            req.Password,
            lockoutOnFailure: true
        );

        if (result.IsLockedOut)
            return Unauthorized(new { code = "LOCKED_OUT" });

        if (result.RequiresTwoFactor)
            return Ok(new { requiresMfa = true });

        if (!result.Succeeded)
            return Unauthorized(new { code = "INVALID_CREDENTIALS" });

        // Build extra claims: tenant_id + permissions
        var extraClaims = await BuildExtraClaimsAsync(user);

        await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);
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

        var extraClaims = await BuildExtraClaimsAsync(user);
        await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);
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
        var extraClaims = await BuildExtraClaimsAsync(user);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.Name, user.Email!)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(extraClaims);

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:SigningKey"]!)
        );

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        );

        return Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token) });
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

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId      = User.FindFirstValue(ClaimTypes.NameIdentifier),
            email       = User.Identity?.Name,
            roles       = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray(),
            tenantId    = User.FindFirstValue("tenant_id"),
            permissions = User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToArray()
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves tenant_id and permission claims for a user.
    /// Platform roles (SuperAdmin etc.) get permissions from PermissionMatrix directly.
    /// Enterprise users also get their tenant_id from EnterpriseUser.EnterpriseAccountId.
    /// </summary>
    private async Task<List<Claim>> BuildExtraClaimsAsync(ApplicationUser user)
    {
        var claims = new List<Claim>();

        // 1. Platform role permissions (from ASP.NET Identity roles)
        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            if (PermissionMatrix.ByRole.TryGetValue(role, out var rolePerms))
                claims.AddRange(rolePerms.Select(p => new Claim("permission", p)));
        }

        // 2. Enterprise tenant + enterprise role permissions
        //    EnterpriseUser.UserId links back to ApplicationUser.Id
        //    Use IgnoreQueryFilters so platform users can also load this if needed
        var enterpriseUser = await _db.EnterpriseUsers
            .Include(eu => eu.Role)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(eu => eu.UserId == user.Id && !eu.IsDeleted);

        if (enterpriseUser != null)
        {
            claims.Add(new Claim("tenant_id", enterpriseUser.EnterpriseAccountId.ToString()));

            if (enterpriseUser.Role != null &&
                PermissionMatrix.ByRole.TryGetValue(enterpriseUser.Role.Name, out var entPerms))
            {
                claims.AddRange(entPerms.Select(p => new Claim("permission", p)));
            }
        }

        // Deduplicate — same permission may come from both platform role and enterprise role
        return claims
            .GroupBy(c => c.Type + "|" + c.Value)
            .Select(g => g.First())
            .ToList();
    }

    private static string GenerateQrCodeUri(string email, string key)
        => $"otpauth://totp/Saqqara:{email}?secret={key}&issuer=Saqqara";
}