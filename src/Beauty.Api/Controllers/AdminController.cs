using Beauty.Api.Data;
using Beauty.Api.Models;
using Beauty.Api.Models.ApprovalHistory;
using Beauty.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Beauty.Api.Controllers
{

    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("admin")]
    public class AdminController : ControllerBase
    {
        private readonly UserApprovalService _approvalService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BeautyDbContext _db;
        public AdminController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [HttpGet("pending-users")]
        public IActionResult GetPendingUsers()
        {
            var users = _userManager.Users
                .Where(u => u.Status == "Pending")
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.Status,
                    Role = "" // optional: fill later
                })
                .ToList();

            return Ok(users);
        }

        public AdminController(
                UserManager<ApplicationUser> userManager,
                UserApprovalService approvalService)
        {
            _userManager = userManager;
            _approvalService = approvalService;
        }

        [HttpPost("approve/{id}")]
        public async Task<IActionResult> Approve(string id)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            await _approvalService.ApproveUserAsync(user, adminId);

            return Ok();
        }

        [HttpPost("reject/{id}")]
        public async Task<IActionResult> Reject(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            user.Status = "Rejected";
            await _userManager.UpdateAsync(user);

            return Ok();
        }

    }
}