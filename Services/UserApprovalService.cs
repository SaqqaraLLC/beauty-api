using Microsoft.AspNetCore.Identity;
using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;

namespace Beauty.Api.Services;

public class UserApprovalService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BeautyDbContext _db;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UserApprovalService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        BeautyDbContext db)


    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    public async Task ApproveUserAsync(
        ApplicationUser user,
        string adminId)
    {
        // 1. Update status
        user.Status = "Approved";
        await _userManager.UpdateAsync(user);

        if (!await _roleManager.RoleExistsAsync("Client"))
        {
            await _roleManager.CreateAsync(new IdentityRole("Client"));
        }

        // 2. Assign Client role

        var result = await _userManager.AddToRoleAsync(user, "Client");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to add user to Client role: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

       

        // 3. Persist audit record
        _db.ApprovalHistories.Add(new ApprovalHistory
        {
            TargetUserId = user.Id,
            PerformedByUserId = adminId,
            Action = "Approved",
            PerformedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}
