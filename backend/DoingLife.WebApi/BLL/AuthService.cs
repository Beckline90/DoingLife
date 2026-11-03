using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
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

     

        public AuthService(UserManager<User> userManager, IEmailService emailService, AppDbContext context, IConfiguration configuration, ITokenService tokenService, IOptions<JwtSettings> jwtSettings, IPasswordHasher<User> passwordHasher)
        {
            _userManager = userManager;
            _emailService = emailService;
            _context = context;
            _config = configuration;
            _tokenService = tokenService;
            _jwtSettings = jwtSettings.Value;
            _passwordHasher = passwordHasher;
        }

        public async Task<SResult> RegisterAsync(RegisterRequestView request)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dateOfBirth = request.DateOfBirth!.Value;
            var age = AgeHelper.CalculateAge(dateOfBirth, today);

            if (dateOfBirth > today || age > AppRules.MaxAge)
            {
                return SResult.Failure("Please enter a valid date of birth.");
            }

            if (age < AppRules.MinAge)
            {
                return SResult.Failure(
                    "You must be 18 or older to create an account. Support for younger users with parental consent is coming soon.");
            }

            if (!request.AcceptedTerms)
            {
                return SResult.Failure("You must accept the Terms of Service and Privacy Policy.");
            }
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                return SResult.Failure("Email is already registered.");
            }
            
            var user = new User
            {
                UserName = request.Email,
                Email = request.Email.ToLowerInvariant(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(), 
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            // The user, profile and preference rows are saved together or not at all.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return SResult.Failure(result.Errors.Select(e => e.Description).ToArray());
            }

            _context.UserProfiles.Add(new UserProfile { UserId = user.Id, DateOfBirth = request.DateOfBirth });
            _context.UserPreferences.Add(new UserPreference { UserId = user.Id, TimeZone = "UTC"});
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();
            await SendConfirmationEmailAsync(user);
            return SResult.Success();
        }

        public async Task<SResult> ConfirmEmailAsync(int userId, string token)
        {
            var invalid = SResult.Failure(new[] { "This Confirmation link is invalid or has expired." });
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
            // Check if email is empty or null
            if (string.IsNullOrWhiteSpace(email))
            {
                return SResult.Success();
            }

            // Find the user by email
            var user = await _userManager.FindByEmailAsync(email.Trim());

            // Only send confirmation if user exists
            // and email has not been confirmed
            if (user != null && !user.EmailConfirmed)
            {
                await SendConfirmationEmailAsync(user);
            }

            return SResult.Success();
        }

        private async Task SendConfirmationEmailAsync(User user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return;
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var baseUrl = _config["App:BaseUrl"];
            var link = $"{baseUrl}/api/auth/confirm-email?userId={user.Id}&token={encodedToken}";

            var body = $"""
            <p>Hi {WebUtility.HtmlEncode(user.FirstName)},</p>
            <p>Please confirm your email to start using Doing Life:</p>
            <p><a href="{link}">Confirm my email</a></p>
            """;

            await _emailService.SendAsync(user.Email, "Confirm your email", body);
        }

        public async Task<SResult<AuthResponseView>> LoginAsync(LoginRequestView request, ClientInfoView clientInfo)
        {
            const string genericError = "Invalid email or password.";


            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
                return SResult<AuthResponseView>.Failure(genericError);

            var user = await _userManager.FindByEmailAsync(request.Email.Trim());

            if (user == null)
            {
                // Do comparable work to a real password check, so an unknown email
                // takes about as long to answer as a wrong password.
                _passwordHasher.HashPassword(new User(), request.Password);

                await RecordLoginAttemptAsync(null, false, "Unknown email", clientInfo);
                return SResult<AuthResponseView>.Failure(genericError);
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                await RecordLoginAttemptAsync(user.Id, false, "Account locked", clientInfo);
                return SResult<AuthResponseView>.Failure(
                    "Account temporarily locked. Please try again later.");
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                await _userManager.AccessFailedAsync(user);
                await RecordLoginAttemptAsync(user.Id, false, "Wrong password", clientInfo);
                return SResult<AuthResponseView>.Failure(genericError);
            }

            // Correct password: clear the failed-attempt counter
            await _userManager.ResetAccessFailedCountAsync(user);

            if (!user.IsActive || user.IsDeleted)
            {
                await RecordLoginAttemptAsync(user.Id, false, "Inactive or deleted account", clientInfo);
                return SResult<AuthResponseView>.Failure(
                    "This account is unavailable. Please contact support.");
            }

            if (!user.EmailConfirmed)
            {
                await RecordLoginAttemptAsync(user.Id, false, "Email not confirmed", clientInfo);
                return SResult<AuthResponseView>.Failure(
                    "Please verify your email before logging in.");
            }

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

            _context.UserLoginHistories.Add(BuildHistory(user.Id, true, null, clientInfo));
            await _context.SaveChangesAsync();

            return SResult<AuthResponseView>.Success(new AuthResponseView
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
            });
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
