using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Beauty.Api.Tests;

// ── Test stubs ──────────────────────────────────────────────────────────────

sealed class PlatformTenant : ITenantContext
{
    public Guid? CurrentTenantId => null;
    public bool IsPlatformUser => true;
}

sealed class NullEmailSender : IEmailSender
{
    public Task SendHtmlAsync(string to, string subject, string html, string? fromOverride = null) => Task.CompletedTask;
    public Task SendHtmlWithAttachmentAsync(string to, string subject, string html, string fileName, byte[] content, string contentType, string? fromOverride = null) => Task.CompletedTask;
}

sealed class NullTemplateRenderer : ITemplateRenderer
{
    public string Render(string templateName, IDictionary<string, string> model) => string.Empty;
}

// ── Tests ───────────────────────────────────────────────────────────────────

public class AdminApprovalTests
{
    [Fact]
    public async Task Approving_User_Sets_Status_And_Writes_Audit_Record()
    {
        var options = new DbContextOptionsBuilder<BeautyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new BeautyDbContext(options, new PlatformTenant());

        var userStore = new UserStore<ApplicationUser>(db);
        var roleStore = new RoleStore<IdentityRole>(db);

        var roleManager = new RoleManager<IdentityRole>(
            roleStore,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null
        );

        var userManager = new UserManager<ApplicationUser>(
            userStore,
            null,
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            null
        );

        var emailService = new EmailTemplateService(new NullEmailSender(), new NullTemplateRenderer());

        // Seed roles
        await roleManager.CreateAsync(new IdentityRole("Admin"));
        await roleManager.CreateAsync(new IdentityRole("Client"));

        var approvalService = new UserApprovalService(
            userManager,
            roleManager,
            db,
            emailService
        );

        var admin = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            Status = "Approved"
        };

        await userManager.CreateAsync(admin);
        await userManager.AddToRoleAsync(admin, "Admin");

        var pendingUser = new ApplicationUser
        {
            UserName = "user@test.com",
            Email = "user@test.com",
            Status = "Pending"
        };

        await userManager.CreateAsync(pendingUser);
        await approvalService.ApproveUserAsync(pendingUser, admin.Id);

        // Assert role
        var refreshedUser = await userManager.FindByIdAsync(pendingUser.Id);
        var roles = await userManager.GetRolesAsync(refreshedUser!);
        Assert.Contains("Client", roles);

        // Assert status
        Assert.Equal("Approved", refreshedUser!.Status);

        // Assert audit
        var audit = db.ApprovalHistories.Single();
        Assert.Equal("Approved", audit.Action);
    }
}
