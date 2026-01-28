using eSim.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eSim.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        public AppDbContext _dbContext { get; }
        public ILogger<UserController> _logger { get; }

        public UserController(AppDbContext dbContext, ILogger<UserController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        [HttpGet("GetUserList")]
        public async Task<IActionResult> GetUserList(CancellationToken ct)
        {
            try
            {
                var users = await _dbContext.Users.ToListAsync(ct);

                if (users == null || users.Count == 0)
                {
                    return NotFound("No users found.");
                }

                var userList = users.Select(u => new
                {
                    u.Email,
                    u.CountryOfResidence,
                    u.FullName
                }).ToList();

                return Ok(userList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching user list.");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
