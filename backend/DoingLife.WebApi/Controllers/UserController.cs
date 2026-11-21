using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection.Metadata.Ecma335;

namespace DoingLife.WebApi.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IUserService _userService;

        public UsersController(UserManager<User> userManager, IUserService userService)
        {
            _userManager = userManager;
            _userService = userService;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var id = TryGetUserId();
            if (id == null)
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(id.Value.ToString());

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

        // The whole profile. No id in the route: the id only ever comes from the token.
        [HttpGet("me/profile")]
        public async Task<IActionResult> GetProfile()
        {
            var id = TryGetUserId();
            if (id == null)
            {
                return Unauthorized();
            }

            var result = await _userService.GetProfileAsync(id.Value);
            return ToResponse(result);
        }

        // PUT replaces the whole profile. The client always sends all three fields,
        // and a missing or blank bio means "no bio".
        [HttpPut("me/profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileUpdateView request)
        {
            var id = TryGetUserId();
            if (id == null)
            {
                return Unauthorized();
            }

            var result = await _userService.UpdateProfileAsync(id.Value, request);
            return ToResponse(result);
        }

        // returns the user preference 
        [HttpGet("me/preference")]
        public async Task<IActionResult> GetPreferences()
        {
            var id = TryGetUserId();
            if (id == null)
            {
                return Unauthorized();
            }
            var result = await _userService.GetPreferencesAsync(id.Value);
            return ToResponse(result);

        }

        //patch only the settings that were sent
        [HttpPatch("me/preferences")]
        public async Task<IActionResult> UpdatePreference([FromBody] PreferenceUpdateView request)
        {
            var id = TryGetUserId();
            if(id == null)
            {
                return Unauthorized();
            }
            var result = await _userService.UpdatePreferencesAsync(id.Value, request);
            return ToResponse(result);

        }


        #region --- helpers ---
        // Who is asking? Reads the id from the signed token, never from the request.
        // Returns null when the claim is missing or isn't a number.
        private int? TryGetUserId()
        {
            var value = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(value, out var id) ? id : null;
        }

        // What does the client see? The one place that maps a service result to HTTP.
        private IActionResult ToResponse<T>(SResult<T> result)
        {
            if (result.Succeeded)
            {
                return Ok(result.Data);
            }

            var body = new { code = result.ErrorCode, errors = result.Errors };

            return result.ErrorCode switch
            {
                // The token is valid, but the account is no longer allowed. The client should sign in again.
                ErrorCodes.AccountUnavailable => Unauthorized(body),

                // The client sent something we do not accept
                ErrorCodes.InvalidName => BadRequest(body),
                ErrorCodes.InvalidLanguage => BadRequest(body),
                ErrorCodes.InvalidTimeZone => BadRequest(body),

                // The data changed while saving. The client should reload and try again.
                ErrorCodes.Conflict => Conflict(body),

                // Faults on our side, not the client's mistake. The log holds the detail.
                ErrorCodes.FailedProfile => StatusCode(StatusCodes.Status500InternalServerError, body),
                ErrorCodes.MissingPreferences => StatusCode(StatusCodes.Status500InternalServerError, body),

                // Anything we did not plan for is also our fault
                _ => StatusCode(StatusCodes.Status500InternalServerError, body)
            };
        }
        #endregion
    }
}