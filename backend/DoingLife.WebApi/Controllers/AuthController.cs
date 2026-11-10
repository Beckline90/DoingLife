using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace DoingLife.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // Register a new user
    [HttpPost("register")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestView request)
    {
        var result = await _authService.RegisterAsync(request);
        if (!result.Succeeded)
        {
            return BadRequest(new { code = result.ErrorCode, errors = result.Errors });
        }

        return Ok(new
        {
            message = "Account created. Please check your email to verify your account."
        });
    }

    // Called by the front-end verify-email page
    [HttpPost("confirm-email")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> ConfirmEmailFromApp([FromBody] ConfirmEmailRequestView request)
    {
        var result = await _authService.ConfirmEmailAsync(request.UserId, request.Token);

        if (!result.Succeeded)
        {
            return BadRequest(new { code = result.ErrorCode, errors = result.Errors });
        }
        return Ok(new { message = "Email verified." });
    }

    // Resend email confirmation link
    [HttpPost("resend-confirmation")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> ResendConfirmation(
        [FromBody] EmailRequestView request)
    {
        await _authService.ResendConfirmationAsync(request.Email);

        return Ok(new
        {
            message = "If an unverified account exists with this email, a confirmation link will be sent."
        });
    }
    // Log in with email and password
    [HttpPost("login")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> Login([FromBody] LoginRequestView request)
    {
        var clientInfo = new ClientInfoView
        {
            ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        };

        var result = await _authService.LoginAsync(request, clientInfo);

        if (!result.Succeeded)
        {
            return LoginFailure(result);
        }
        return Ok(result.Data);
    }

    // Swap a refresh token for a new access token and a new refresh token
    [HttpPost("refresh")]
    [EnableRateLimiting("RefreshRateLimit")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestView request)
    {
        var result = await _authService.RefreshTokenAsync(request.Refreshtoken);

        if (!result.Succeeded)
        {
            return Unauthorized(new { errors = result.Errors });
        }

        return Ok(result.Data);
    }

    // End one session. No [Authorize] on purpose: the access token has often
    // expired by the time someone logs out, and the refresh token is the proof.
    [HttpPost("logout")]

    [EnableRateLimiting("RefreshRateLimit")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestView request)
    {
        await _authService.LogoutAsync(request.Refreshtoken);
        return Ok(new { message = "Logged out." });
    }

    // End every session for the signed-in user (log out of all devices)
    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        var sub = HttpContext.User.FindFirst("sub")?.Value;

        if (!int.TryParse(sub, out var userId))
        {
            return Unauthorized();
        }

        await _authService.LogoutAllAsync(userId);
        return Ok(new { message = "Logged out of all devices." });
    }

    // Start a password reset: always answers the same way
    [HttpPost("forgot-password")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> ForgotPassword([FromBody] EmailRequestView request)
    {
        await _authService.ForgotPasswordAsync(request.Email);

        return Ok(new
        {
            message = "If an account exists with this email, a reset link will be sent."
        });
    }

    // Finish a password reset from the front-end page
    [HttpPost("reset-password")]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestView request)
    {
        var result = await _authService.ResetPasswordAsync(
            request.UserId, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            return BadRequest(new { code = result.ErrorCode, errors = result.Errors });
        }

        return Ok(new { message = "Password updated. Please log in." });
    }




    private IActionResult LoginFailure(SResult result)
    {
        var body = new { code = result.ErrorCode, errors = result.Errors };

        return result.ErrorCode switch
        {
            ErrorCodes.EmailNotVerified or ErrorCodes.AccountUnavailable
                => StatusCode(StatusCodes.Status403Forbidden, body),
            ErrorCodes.AccountLocked
                => StatusCode(StatusCodes.Status423Locked, body),
            _ => Unauthorized(body)
        };
    }

}