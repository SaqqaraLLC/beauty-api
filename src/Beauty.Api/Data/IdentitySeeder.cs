
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        "Staff",
        "Director"
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();


        // Ensure roles
        foreach (var roleName in Roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(
                    new ApplicationRole { Name = roleName });
            }
        }

        // Admin user
        var adminEmail = config["Seed:AdminEmail"];
        var adminPassword = config["Seed:AdminPassword"];

        if (!string.IsNullOrWhiteSpace(adminEmail) &&
            !string.IsNullOrWhiteSpace(adminPassword))
        {
            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FirstName = "Admin",
                    LastName = "User",
                    IsActive = true
                };

                await userManager.CreateAsync(admin, adminPassword);
                await userManager.AddToRoleAsync(admin, "Admin");
                
            }
        }

        // Staff → Director
        var staffEmail = config["Seed:StaffEmail"];
        var staffPassword = config["Seed:StaffPassword"];

        if (!string.IsNullOrWhiteSpace(staffEmail) &&
            !string.IsNullOrWhiteSpace(staffPassword))
        {
            var staff = await userManager.FindByEmailAsync(staffEmail);
            if (staff == null)
            {
                staff = new ApplicationUser
                {
                    UserName = staffEmail,
                    Email = staffEmail,
                    EmailConfirmed = true,
                    FirstName = "Staff",
                    LastName = "User",
                    IsActive = true
                };

                await userManager.CreateAsync(staff, staffPassword);
                await userManager.AddToRoleAsync(staff, "Director");
            }

            if (!await userManager.IsInRoleAsync(staff, "Director"))
            {
                await userManager.AddToRoleAsync(staff, "Director");
            }
        }
    }
}
       

