using Microsoft.AspNetCore.Identity;
using Beauty.Api.Data;
using Beauty.Api.Email;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;

namespace Beauty.Api.Services;

public class UserApprovalService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BeautyDbContext _db;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly EmailTemplateService _email;

    public UserApprovalService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        BeautyDbContext db,
        EmailTemplateService email)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _email = email;
    }

    public async Task ApproveUserAsync(ApplicationUser user, string adminId)
    {
        user.Status = "Approved";
        await _userManager.UpdateAsync(user);

        // Use whatever role the user was assigned at registration
        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Client";

        if (!await _roleManager.RoleExistsAsync(role))
            await _roleManager.CreateAsync(new IdentityRole(role));

        // Confirm their existing role (already assigned at register) is active
        if (!await _userManager.IsInRoleAsync(user, role))
            await _userManager.AddToRoleAsync(user, role);

        _db.ApprovalHistories.Add(new ApprovalHistory
        {
            TargetUserId = user.Id,
            PerformedByUserId = adminId,
            Action = "Approved",
            PerformedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Send approval email (fire and forget)
        _ = _email.SendApprovedAsync(user.Email!, role).ContinueWith(_ => { });
    }

    public async Task RejectUserAsync(ApplicationUser user, string adminId, string reason)
    {
        user.Status = "Rejected";
        await _userManager.UpdateAsync(user);

        _db.ApprovalHistories.Add(new ApprovalHistory
        {
            TargetUserId = user.Id,
            PerformedByUserId = adminId,
            Action = "Rejected",
            PerformedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Send rejection email (fire and forget)
        _ = _email.SendRejectedAsync(user.Email!, reason).ContinueWith(_ => { });
    }
}
