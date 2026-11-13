using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text;

namespace DoingLife.WebApi.BLL
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly IEmailService _emailService;
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly ITokenService _tokenService;
        private readonly JwtSettings _jwtSettings;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            UserManager<User> userManager,
            IEmailService emailService,
            AppDbContext context,
            IConfiguration configuration,
            ITokenService tokenService,
            IOptions<JwtSettings> jwtSettings,
            IPasswordHasher<User> passwordHasher,
            ILogger<AuthService> logger)
        {
            _userManager = userManager;
            _emailService = emailService;
            _context = context;
            _config = configuration;
            _tokenService = tokenService;
            _jwtSettings = jwtSettings.Value;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public async Task<SResult> RegisterAsync(RegisterRequestView request)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dateOfBirth = request.DateOfBirth!.Value;
            var age = AgeHelper.CalculateAge(dateOfBirth, today);

            if (dateOfBirth > today || age > AppRules.MaxAge)
            {
                return SResult.Failure("Please enter a valid date of birth.", ErrorCodes.InvalidDateOfBirth);
            }

            if (age < AppRules.MinAge)
            {
                return SResult.Failure("You must be 18 or older to create an account. Support for younger users with parental consent is coming soon.",ErrorCodes.UnderAge);
            }

            if (!request.AcceptedTerms)
            {
                return SResult.Failure("You must accept the Terms of Service and Privacy Policy.", ErrorCodes.TermsNotAccepted);
            }

            // Normalize once and use the same value everywhere
            var email = request.Email.Trim().ToLowerInvariant();

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                return SResult.Failure("Email is already registered.", ErrorCodes.EmailAlreadyRegistered);
            }

            var user = new User
            {
                UserName = email,
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // The user, profile and preference rows are saved together or not at all.
            // If we return early, disposing the transaction rolls it back.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var code = result.Errors.Any(e => e.Code.StartsWith("Password")) ? ErrorCodes.WeakPassword : null;
                return SResult.Failure(result.Errors.Select(e => e.Description).ToArray(), code);
            }

            _context.UserProfiles.Add(new UserProfile { UserId = user.Id, DateOfBirth = request.DateOfBirth });
            _context.UserPreferences.Add(new UserPreference { UserId = user.Id, TimeZone = "UTC" });
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            // The account exists now, so a failed email must not fail registration.
            await SendConfirmationEmailAsync(user);
            return SResult.Success();
        }

        public async Task<SResult> ConfirmEmailAsync(int userId, string token)
        {
            var invalid = SResult.Failure("This confirmation link is invalid or has expired.", ErrorCodes.InvalidToken);

            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                return invalid;
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch (FormatException)
            {
                return invalid;
            }

            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
            return result.Succeeded ? SResult.Success() : invalid;
        }

        public async Task<SResult> ResendConfirmationAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return SResult.Success();
            }

            var user = await _userManager.FindByEmailAsync(email.Trim());

            // Always return success so nobody can probe which emails are registered.
            if (user != null && !user.EmailConfirmed)
            {
                await SendConfirmationEmailAsync(user);
            }

            return SResult.Success();
        }

        public async Task<SResult<AuthResponseView>> LoginAsync(LoginRequestView request, ClientInfoView clientInfo)
        {
            var invalidCredentials = SResult<AuthResponseView>.Failure(
                "Invalid email or password.", ErrorCodes.InvalidCredentials);

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
                return invalidCredentials;

            var user = await _userManager.FindByEmailAsync(request.Email.Trim());

            if (user == null)
            {
                // Do comparable work to a real password check, so an unknown email
                // takes about as long to answer as a wrong password.
                _passwordHasher.HashPassword(new User(), request.Password);

                await RecordLoginAttemptAsync(null, false, "Unknown email", clientInfo);
                return invalidCredentials;
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                await RecordLoginAttemptAsync(user.Id, false, "Account locked", clientInfo);
                return SResult<AuthResponseView>.Failure(
                    "Account temporarily locked. Please try again later.", ErrorCodes.AccountLocked);
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                var failed = await _userManager.AccessFailedAsync(user);
                if (!failed.Succeeded)
                {
                    _logger.LogWarning("Could not record a failed login for user {UserId}", user.Id);
                }

                await RecordLoginAttemptAsync(user.Id, false, "Wrong password", clientInfo);
                return invalidCredentials;
            }

            // Correct password: clear the failed-attempt counter
            var reset = await _userManager.ResetAccessFailedCountAsync(user);
            if (!reset.Succeeded)
            {
                _logger.LogWarning("Could not reset the failed-login counter for user {UserId}", user.Id);
            }

            if (!user.IsActive || user.IsDeleted)
            {
                await RecordLoginAttemptAsync(user.Id, false, "Inactive or deleted account", clientInfo);
                return SResult<AuthResponseView>.Failure(
                    "This account is unavailable. Please contact support.", ErrorCodes.AccountUnavailable);
            }

            if (!user.EmailConfirmed)
            {
                await RecordLoginAttemptAsync(user.Id, false, "Email not confirmed", clientInfo);
                return SResult<AuthResponseView>.Failure(
                    "Please verify your email before logging in.", ErrorCodes.EmailNotVerified);
            }

            _context.UserLoginHistories.Add(BuildHistory(user.Id, true, null, clientInfo));

            var response = await IssueTokensAsync(user);
            await _context.SaveChangesAsync();

            return SResult<AuthResponseView>.Success(response);
        }
        public async Task<SResult<AuthResponseView>> RefreshTokenAsync(string refreshToken)
        {
            const string invalid = "Invalid or expired session. Please log in again.";

            if (string.IsNullOrWhiteSpace(refreshToken))
                return SResult<AuthResponseView>.Failure(invalid, ErrorCodes.InvalidSession);

            var hash = _tokenService.HashRefreshToken(refreshToken);
            var session = await _context.UserSessions
                .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash);

            if (session == null)
                return SResult<AuthResponseView>.Failure(invalid, ErrorCodes.InvalidSession);

            // A revoked token was presented again: either a client bug or a stolen token.
            // End every session for this user so the thief's copy stops working too.
            if (session.RevokedAt != null)
            {
                await RevokeAllSessionsAsync(session.UserId);
                _logger.LogWarning("A revoked refresh token was reused. All sessions revoked for user {UserId}", session.UserId);
                return SResult<AuthResponseView>.Failure(invalid, ErrorCodes.InvalidSession);
            }

            var now = DateTime.UtcNow;
            if (session.ExpiresAt <= now)
                return SResult<AuthResponseView>.Failure(invalid, ErrorCodes.InvalidSession);

            // The account must still be allowed to sign in
            var user = await _userManager.FindByIdAsync(session.UserId.ToString());
            if (user == null
                || !user.IsActive
                || user.IsDeleted
                || !user.EmailConfirmed
                || await _userManager.IsLockedOutAsync(user))
            {
                await RevokeSessionAsync(session.UserSessionId);
                return SResult<AuthResponseView>.Failure(invalid, ErrorCodes.InvalidSession);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            // Claim the old session with one update that only succeeds if it is still
            // unrevoked. If two requests race with the same token, only one gets 1 row.
            var claimed = await _context.UserSessions
                .Where(s => s.UserSessionId == session.UserSessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTime?)now));

            if (claimed == 0)
            {
                // Roll back first, otherwise the revoke below would be undone with it
                await transaction.RollbackAsync();
                await RevokeAllSessionsAsync(session.UserId);
                _logger.LogWarning("Concurrent reuse of a refresh token. All sessions revoked for user {UserId}", session.UserId);
                return SResult<AuthResponseView>.Failure(invalid, ErrorCodes.InvalidSession);
            }

            var response = await IssueTokensAsync(user);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return SResult<AuthResponseView>.Success(response);
        }

        public async Task<SResult> LogoutAsync(string refreshToken)
        {
            // Always succeed, so logging out twice is harmless and reveals nothing
            if (string.IsNullOrWhiteSpace(refreshToken))
                return SResult.Success();

            var hash = _tokenService.HashRefreshToken(refreshToken);

            // Delete the session instead of revoking it. If the client later sends this
            // token to /refresh by mistake, it finds nothing and fails quietly, instead of
            // being treated as theft and signing the user out of every device.
            await _context.UserSessions
                .Where(s => s.RefreshTokenHash == hash)
                .ExecuteDeleteAsync();

            return SResult.Success();
        }

        public async Task<SResult> LogoutAllAsync(int userId)
        {
            await RevokeAllSessionsAsync(userId);
            return SResult.Success();
        }

        // Builds the tokens and ADDS the new session, but does not save.
        // The caller saves, so login and refresh can each do it as one unit.
        private async Task<AuthResponseView> IssueTokensAsync(User user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var (accessToken, accessTokenExpiresAt) = _tokenService.CreateAccessToken(user, roles);
            var refreshToken = _tokenService.GenerateRefreshToken();
            var now = DateTime.UtcNow;

            // Only the hash of the refresh token is stored
            _context.UserSessions.Add(new UserSession
            {
                UserId = user.Id,
                RefreshTokenHash = _tokenService.HashRefreshToken(refreshToken),
                CreatedAt = now,
                ExpiresAt = now.AddDays(_jwtSettings.RefreshTokenExpirationDays)
            });

            return new AuthResponseView
            {
                AccessToken = accessToken,
                AccessTokenExpireAt = accessTokenExpiresAt,
                RefreshToken = refreshToken,
                User = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email ?? string.Empty,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    EmailConfirmed = user.EmailConfirmed
                }
            };
        }

        private async Task RevokeAllSessionsAsync(int userId)
        {
            var now = DateTime.UtcNow;
            await _context.UserSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTime?)now));
        }

        private async Task RevokeSessionAsync(int sessionId)
        {
            var now = DateTime.UtcNow;
            await _context.UserSessions
                .Where(s => s.UserSessionId == sessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTime?)now));
        }


        private async Task SendConfirmationEmailAsync(User user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return;

            try
            {
                var frontendUrl = _config["App:FrontendUrl"];
                if (string.IsNullOrWhiteSpace(frontendUrl))
                {
                    _logger.LogError("App:FrontendUrl is not configured, so no confirmation link can be built.");
                    return;
                }

                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var link = $"{frontendUrl.TrimEnd('/')}/verify-email?userId={user.Id}&token={encodedToken}";

                var body = $"""
        <p>Hi {WebUtility.HtmlEncode(user.FirstName)},</p>
        <p>Please confirm your email to start using Doing Life:</p>
        <p><a href="{link}">Confirm my email</a></p>
        """;

                await _emailService.SendAsync(user.Email, "Confirm your email", body);
            }
            catch (Exception ex)
            {
                // Never let an email problem break registration or reveal that an account exists.
                // The user can ask for another email with the resend endpoint.
                _logger.LogError(ex, "Could not send the confirmation email for user {UserId}", user.Id);
            }
        }

        public async Task<SResult> ForgotPasswordAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return SResult.Success();
            }

            var user = await _userManager.FindByEmailAsync(email.Trim());

            // Same answer whether or not the account exists, so nobody can probe emails.
            if (user != null && user.IsActive && !user.IsDeleted)
            {
                await SendPasswordResetEmailAsync(user);
            }

            return SResult.Success();
        }

        public async Task<SResult> ResetPasswordAsync(int userId, string token, string newPassword)
        {
            var invalid = SResult.Failure("This reset link is invalid or has expired.", ErrorCodes.InvalidToken);

            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null || !user.IsActive || user.IsDeleted)
            {
                return invalid;
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch (FormatException)
            {
                return invalid;
            }

            var result = await _userManager.ResetPasswordAsync(user, decodedToken, newPassword);
            if (!result.Succeeded)
            {
                // A bad token and a weak password need different messages
                if (result.Errors.Any(e => e.Code == "InvalidToken"))
                {
                    return invalid;
                }

                return SResult.Failure(
                    result.Errors.Select(e => e.Description).ToArray(),
                    ErrorCodes.WeakPassword);
            }

            // The token was delivered to this inbox, so it also proves the email is theirs
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            // Lift any lockout and end every existing session on every device
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
            await RevokeAllSessionsAsync(user.Id);

            _logger.LogInformation("Password was reset for user {UserId}", user.Id);
            return SResult.Success();
        }

        private async Task SendPasswordResetEmailAsync(User user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return;

            try
            {
                var frontendUrl = _config["App:FrontendUrl"];
                if (string.IsNullOrWhiteSpace(frontendUrl))
                {
                    _logger.LogError("App:FrontendUrl is not configured, so no reset link can be built.");
                    return;
                }

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var link = $"{frontendUrl.TrimEnd('/')}/reset-password?userId={user.Id}&token={encodedToken}";

                var body = $"""
        <p>Hi {WebUtility.HtmlEncode(user.FirstName)},</p>
        <p>We received a request to reset your Doing Life password. The link below works once:</p>
        <p><a href="{link}">Reset my password</a></p>
        <p>If you didn't ask for this, you can ignore this email and your password won't change.</p>
        """;

                await _emailService.SendAsync(user.Email, "Reset your password", body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not send the password reset email for user {UserId}", user.Id);
            }
        }

        private async Task RecordLoginAttemptAsync(int? userId, bool isSuccessful, string? failureReason, ClientInfoView clientInfo)
        {
            _context.UserLoginHistories.Add(BuildHistory(userId, isSuccessful, failureReason, clientInfo));
            await _context.SaveChangesAsync();
        }

        private static UserLoginHistory BuildHistory(int? userId, bool isSuccessful, string? failureReason, ClientInfoView clientInfo)
        {
            return new UserLoginHistory
            {
                UserId = userId,
                LoginProvider = LoginProviders.Password,
                IpAddress = Truncate(clientInfo.ClientIp, 45),
                UserAgent = Truncate(clientInfo.UserAgent, 500),
                IsSuccessful = isSuccessful,
                FailureReason = failureReason,
                AttemptedAt = DateTime.UtcNow
            };
        }

        private static string? Truncate(string? value, int maxLength) =>
            value is null ? null : (value.Length <= maxLength ? value : value[..maxLength]);
    }
}