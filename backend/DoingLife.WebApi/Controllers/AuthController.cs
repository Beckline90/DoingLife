using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Mvc;

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
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestView request)
    {
        var result = await _authService.RegisterAsync(request);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                errors = result.Errors
            });
        }

        return Ok(new
        {
            message = "Account created. Please check your email to verify your account."
        });
    }

    // Confirm user's email address
    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(
        [FromQuery] int userId,
        [FromQuery] string token)
    {
        var result = await _authService.ConfirmEmailAsync(userId, token);

        var title = result.Succeeded
            ? "Email verified"
            : "Verification failed";

        var message = result.Succeeded
            ? "Email verified. You can close this window and return to the app."
            : "This confirmation link is invalid or has expired. Please request a new one from the app.";

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Doing Life - Email Verification</title>
            </head>
            <body style="font-family: sans-serif; text-align: center; margin-top: 15vh;">
                <h2>{title}</h2>
                <p>{message}</p>
            </body>
            </html>
            """;

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = result.Succeeded ? 200 : 400
        };
    }

    // Resend email confirmation link
    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(
        [FromBody] EmailRequestView request)
    {
        await _authService.ResendConfirmationAsync(request.Email);

        return Ok(new
        {
            message = "If an unverified account exists with this email, a confirmation link will be sent."
        });
    }
}