using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Beauty.Api.Tests;

public class AdminApprovalTests
{
    [Fact]
    public async Task Approving_User_Sets_Status_And_Writes_Audit_Record()
    {
        var options = new DbContextOptionsBuilder<BeautyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new BeautyDbContext(options);

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

        // Seed roles
        await roleManager.CreateAsync(new IdentityRole("Admin"));
        await roleManager.CreateAsync(new IdentityRole("Client"));

        var approvalService = new UserApprovalService(
            userManager,
            roleManager,
            db
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

        // ✅ REQUIRED
        await userManager.CreateAsync(pendingUser);

        // ✅ REQUIRED
        await approvalService.ApproveUserAsync(pendingUser, admin.Id);

        // Assert role
        var refreshedUser = await userManager.FindByIdAsync(pendingUser.Id);
        var roles = await userManager.GetRolesAsync(refreshedUser!);
        Assert.Contains("Client", roles);

        // Assert status
        Assert.Equal("Approved", refreshedUser.Status);

        // Assert audit
        var audit = db.ApprovalHistories.Single();
        Assert.Equal("Approved", audit.Action);
    }
}