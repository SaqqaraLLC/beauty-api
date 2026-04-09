
using System.Security.Claims;
using Beauty.Api.Contracts;
using Beauty.Api.Models.ApprovalHistory;

namespace Beauty.Api.Domain.Approvals;

public interface IBookingApprovalService
{
    Task ApproveAsync(
        long bookingId,
        ApprovalStage stage,
        ClaimsPrincipal user);

    Task RejectAsync(
        long bookingId,
        ApprovalStage stage,
        string reason,
        ClaimsPrincipal user);

    IReadOnlyList<PendingUserDto> GetPendingUsers();
}