
using Beauty.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Beauty.Api.Data;

public static class IdentitySeeder
{

    private static readonly string[] Roles =
        {
        "Admin",
        "Staff"
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();

        var adminEmail = config["Seed:AdminEmail"];
        var adminPassword = config["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                "Seed admin credentials are missing from configuration.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        // Ensure Admin role exists

        foreach (var roleName in Roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var role = new ApplicationRole
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant()
                };

                var result = await roleManager.CreateAsync(role);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to create role '{roleName}': " +
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }


        // Ensure admin user exists
        var user = await userManager.FindByEmailAsync(adminEmail);
        if (user == null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true,
                IsActive = true
            };

            var userResult = await userManager.CreateAsync(user, adminPassword);

            if (!userResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to create admin user: " +
                    string.Join(", ", userResult.Errors.Select(e => e.Description)));
            }

            var addRoleResult = await userManager.AddToRoleAsync(user, "Admin");
            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to assign Admin role: " +
                    string.Join(", ", addRoleResult.Errors.Select(e => e.Description)));
            }
        }
        var staffEmail = config["Seed:StaffEmail"];
        var staffPassword = config["Seed:StaffPassword"];

        if (!string.IsNullOrWhiteSpace(staffEmail) &&
            !string.IsNullOrWhiteSpace(staffPassword))
        {
            var staffUser = await userManager.FindByEmailAsync(staffEmail);
            if (staffUser == null)
            {
                staffUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = staffEmail,
                    Email = staffEmail,
                    EmailConfirmed = true,
                    IsActive = true,
                    FirstName = "Staff",
                    LastName = "User"
                };

                var staffResult = await userManager.CreateAsync(staffUser, staffPassword);
                if (!staffResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Failed to create staff user: " +
                        string.Join(", ", staffResult.Errors.Select(e => e.Description)));
                }

                await userManager.AddToRoleAsync(staffUser, "Staff");
            }
        }
    }


}

