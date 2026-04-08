using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Beauty.Api.Controllers
{
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("admin")]
    public class AdminController : ControllerBase
    {
        [HttpGet("pending-users")]
        public IActionResult GetPendingUsers()
        {
            // TEMP: return mock data so frontend works
            var users = new[]
            {
                new {
                    Id = "1",
                    Email = "artist@test.com",
                    Role = "Artist",
                    Status = "Pending"
                },
                new {
                    Id = "2",
                    Email = "client@test.com",
                    Role = "Client",
                    Status = "Pending"
                }
            };

            return Ok(users);
        }

        [HttpPost("approve/{id}")]
        public IActionResult Approve(string id)
        {
            // TEMP: approval logic will go here later
            return Ok(new { Approved = id });
        }

        [HttpPost("reject/{id}")]
        public IActionResult Reject(string id)
        {
            // TEMP: rejection logic will go here later
            return Ok(new { Rejected = id });
        }
    }
}
