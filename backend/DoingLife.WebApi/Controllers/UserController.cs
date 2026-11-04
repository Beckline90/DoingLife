
using DoingLife.WebApi.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace DoingLife.WebApi.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<User> _userManager;

        public UsersController(UserManager<User> userManager)
        {
            _userManager = userManager;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var userId = User.FindFirst(
                JwtRegisteredClaimNames.Sub)?.Value;

            if (!int.TryParse(userId, out var id))
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(
                id.ToString());

            if (user == null || !user.IsActive || user.IsDeleted)
            {
                return Unauthorized();
            }

            return Ok(new
            {
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.EmailConfirmed
            });
        }
    }
}
