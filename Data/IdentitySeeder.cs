using Beauty.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace Beauty.Api.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        const string adminEmail = "k.stephen@saqqarallc.com";
        const string adminPassword = "Admin123!"; // 🔐 change later

        // Ensure role exists
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Admin"
            });
        }

        // Ensure user exists
        var user = await userManager.FindByEmailAsync(adminEmail);

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "Admin");
            }
        }
    }
}
