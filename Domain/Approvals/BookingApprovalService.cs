using Beauty.Api.Contracts;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Beauty.Api.Domain.Approvals;

public sealed class BookingApprovalService : IBookingApprovalService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public BookingApprovalService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public IReadOnlyList<PendingUserDto> GetPendingUsers()
    {
        return _userManager.Users
            .Where(u => u.Status == "Pending")
            .Select(u => new PendingUserDto
            {
                Id = u.Id,
                Email = u.Email!,
                Status = u.Status,
                Role = _userManager
                    .GetRolesAsync(u)
                    .Result
                    .FirstOrDefault()
            })
            .ToList();
    }

    public Task ApproveAsync(
        long bookingId,
        ApprovalStage stage,
        ClaimsPrincipal user)
    {
        // TODO: existing approval logic
        throw new NotImplementedException();
    }

    public Task RejectAsync(
        long bookingId,
        ApprovalStage stage,
        string reason,
        ClaimsPrincipal user)
    {
        // TODO: existing rejection logic
        throw new NotImplementedException();
    }
}

