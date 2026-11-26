using System.Security.Claims;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DoingLife.WebApi.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private readonly IAccountService _accountService;

        public AccountController(IAccountService accountService)
        {
            _accountService = accountService;
        }

        // A wrong CURRENT password is a 400 (see ToResponse), never a 401,
        // so the front end does not mistake a typo for an expired session.
        [HttpPost("me/change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestView request)
        {
            var id = TryGetUserId();
            if (id == null)
            {
                return Unauthorized();
            }

            var result = await _accountService.ChangePasswordAsync(id.Value, request);
            return ToResponse(result);
        }

        // Step 1 of an email change: checks the password, then sends a link to the NEW address.
        // Nothing is saved yet.
        [HttpPost("me/change-email")]
        [EnableRateLimiting("AuthRateLimit")]
        public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequestView request)
        {
            var id = TryGetUserId();
            if (id == null)
            {
                return Unauthorized();
            }

            var result = await _accountService.ChangeEmailAsync(id.Value, request);
            return ToResponse(result);
        }

        // Step 2: the person clicks the link in the new inbox. They may not be signed in
        // (or may be on another device), so this is open and the token is the proof.
        [AllowAnonymous]
        [HttpPost("confirm-email-change")]
        [EnableRateLimiting("AuthRateLimit")]
        public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeRequestView request)
        {
            var result = await _accountService.ConfirmEmailChangeAsync(request);
            return ToResponse(result);
        }

        // POST and not DELETE: many proxies and clients drop the body of a DELETE,
        // and this request needs the password in its body.
        [HttpPost("me/delete-account")]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequestView request)
        {
            var id = TryGetUserId();
            if (id == null)
            {
                return Unauthorized();
            }

            var result = await _accountService.DeleteAccountAsync(id.Value, request);
            return ToResponse(result);
        }

        #region --- helpers ---
        // The id comes only from the signed token, never from the route or the body.
        private int? TryGetUserId()
        {
            var value = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out var id) ? id : null;
        }

        private IActionResult ToResponse(SResult result)
        {
            if (result.Succeeded)
            {
                return Ok();
            }

            var body = new { code = result.ErrorCode, errors = result.Errors };

            return result.ErrorCode switch
            {
                // The account may no longer act, or its sessions were just ended
                ErrorCodes.AccountUnavailable => Unauthorized(body),
                ErrorCodes.InvalidSession => Unauthorized(body),

                // The client sent something we do not accept (a wrong current password is here on purpose)
                ErrorCodes.WrongCurrentPassword => BadRequest(body),
                ErrorCodes.PasswordUnchanged => BadRequest(body),
                ErrorCodes.WeakPassword => BadRequest(body),
                ErrorCodes.EmailUnchanged => BadRequest(body),
                ErrorCodes.InvalidToken => BadRequest(body),

                // The address is taken, or the data changed while saving
                ErrorCodes.EmailAlreadyRegistered => Conflict(body),
                ErrorCodes.Conflict => Conflict(body),

                // Faults on our side
                ErrorCodes.EmailNotSent => StatusCode(StatusCodes.Status500InternalServerError, body),
                ErrorCodes.AccountCloseFailed => StatusCode(StatusCodes.Status500InternalServerError, body),

                _ => StatusCode(StatusCodes.Status500InternalServerError, body)
            };
        }
        #endregion
    }
}