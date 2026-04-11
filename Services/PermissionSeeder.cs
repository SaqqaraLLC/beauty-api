using Beauty.Api.Authorization;
using Beauty.Api.Data;
using Beauty.Api.Models.Enterprise;
using Microsoft.EntityFrameworkCore;

namespace Beauty.Api.Services;

/// <summary>
/// Idempotent seeder — runs at startup to ensure EnterpriseRole and Permission rows
/// exist in the DB. Safe to run on every deploy; skips rows that already exist.
/// </summary>
public static class PermissionSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BeautyDbContext>();

        // ── 1. Seed Permission rows ───────────────────────────────────────────
        var existingPerms = (await db.Permissions
            .Select(p => p.Name)
            .ToListAsync())
            .ToHashSet();

        var newPerms = Permissions.All
            .Where(name => !existingPerms.Contains(name))
            .Select(name => new Permission { Name = name, Description = name })
            .ToList();

        if (newPerms.Count > 0)
        {
            db.Permissions.AddRange(newPerms);
            await db.SaveChangesAsync();
        }

        // Reload full map: name → id
        var permMap = await db.Permissions
            .ToDictionaryAsync(p => p.Name, p => p.Id);

        // ── 2. Seed EnterpriseRole rows ───────────────────────────────────────
        var roleScopes = new Dictionary<string, string>
        {
            [RoleNames.SuperAdmin]        = "Platform",
            [RoleNames.PlatformAdmin]     = "Platform",
            [RoleNames.PlatformSupport]   = "Platform",
            [RoleNames.EnterpriseOwner]   = "Enterprise",
            [RoleNames.EnterpriseManager] = "Enterprise",
            [RoleNames.Artist]            = "Enterprise",
            [RoleNames.Receptionist]      = "Enterprise",
        };

        foreach (var (roleName, scope2) in roleScopes)
        {
            var role = await db.EnterpriseRoles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Name == roleName);

            if (role == null)
            {
                role = new EnterpriseRole { Name = roleName, Scope = scope2 };
                db.EnterpriseRoles.Add(role);
                await db.SaveChangesAsync();
            }

            // Sync permissions for this role
            var targetPerms = PermissionMatrix.ByRole.TryGetValue(roleName, out var perms)
                ? perms.ToHashSet()
                : [];

            var existingRolePermIds = role.RolePermissions
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            // Add missing
            foreach (var permName in targetPerms)
            {
                if (!permMap.TryGetValue(permName, out var permId)) continue;
                if (existingRolePermIds.Contains(permId)) continue;

                db.RolePermissions.Add(new RolePermission
                {
                    RoleId       = role.Id,
                    PermissionId = permId
                });
            }

            await db.SaveChangesAsync();
        }
    }
}
