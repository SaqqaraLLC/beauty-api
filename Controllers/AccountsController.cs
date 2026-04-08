using System.ComponentModel.DataAnnotations;
using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AccountsController : ControllerBase
{
    private readonly BeautyDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly EmailTemplateService _email;

    public AccountsController(
        BeautyDbContext db,
        UserManager<ApplicationUser> userMgr,
        RoleManager<IdentityRole> roleMgr,
        EmailTemplateService email)
    {
        _db = db;
        _users = userMgr;
        _roles = roleMgr;
        _email = email;
    }

    // --------------------------------------------------------------------
    // DTOs
    // --------------------------------------------------------------------
    public class RegisterOwnerRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
        [Required] public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
    }

    // --------------------------------------------------------------------
    // POST /api/auth/register-owner
    // Creates the first Owner/Admin (or another Admin) account.
    // Returns 201 Created with { userId, email }.
    // --------------------------------------------------------------------
    [HttpPost("register-owner")]
    [AllowAnonymous] // keep or change to [Authorize(Roles="Admin")] depending on your flow
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterOwner([FromBody] RegisterOwnerRequest req)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // 1) Prevent duplicates
        var existing = await _users.FindByEmailAsync(req.Email);
        if (existing != null)
            return Conflict(new { message = "An account with this email already exists." });

        // 2) Ensure required roles exist (idempotent)
        await EnsureRoleAsync("Admin");
        // If you plan to use these elsewhere, uncomment as needed:
        // await EnsureRoleAsync("Artist");
        // await EnsureRoleAsync("Location");

        // 3) Create user
        var user = new ApplicationUser
        {
            Email = req.Email,
            UserName = req.Email,
            EmailConfirmed = true, // set true if you don't do email confirmation
            PhoneNumber = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone
        };

        var create = await _users.CreateAsync(user, req.Password);
        if (!create.Succeeded)
        {
            // Collate identity errors for the client (policy: length, digits, casing, etc.)
            var errors = create.Errors.Select(e => new { e.Code, e.Description });
            return BadRequest(new { message = "Unable to create user", errors });
        }

        // 4) Add Admin role
        var addRole = await _users.AddToRoleAsync(user, "Admin");
        if (!addRole.Succeeded)
        {
            var errors = addRole.Errors.Select(e => new { e.Code, e.Description });
            return BadRequest(new { message = "Unable to assign Admin role", errors });
        }

        // 5) Optional welcome email (idempotent; swallow failures if you want a pure 201)
        try
        {
            await _email.SendWelcomeAsync(user.Email!, req.FullName, loginUrl: "https://app.saqqarallc.com/login");
        }
        catch
        {
            // Log if you have ILogger<AccountsController>, but don't fail the 201
        }

        return Created($"/api/users/{user.Id}", new { userId = user.Id, email = user.Email });
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------
    private async Task EnsureRoleAsync(string roleName)
    {
        if (!await _roles.RoleExistsAsync(roleName))
        {
            var result = await _roles.CreateAsync(new ApplicationRole { Name = roleName });
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
                throw new InvalidOperationException($"Failed creating role '{roleName}': {errors}");
            }
        }
    }
}

