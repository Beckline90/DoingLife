
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
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

        public AuthService(UserManager<User> userManager, IEmailService emailService, AppDbContext context, IConfiguration configuration)
        {
            _userManager = userManager;
            _emailService = emailService;
            _context = context;
            _config = configuration;
        }

        public async Task<ServiceResult> RegisterAsync(RegisterRequestView request)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dateOfBirth = request.DateOfBirth!.Value;
            var age = AgeHelper.CalculateAge(dateOfBirth, today);

            if (dateOfBirth > today || age > AppRules.MaxAge)
            {
                return ServiceResult.Failure("Please enter a valid date of birth.");
            }

            if (age < AppRules.MinAge)
            {
                return ServiceResult.Failure(
                    "You must be 18 or older to create an account. Support for younger users with parental consent is coming soon.");
            }

            if (!request.AcceptedTerms)
            {
                return ServiceResult.Failure("You must accept the Terms of Service and Privacy Policy.");
            }
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                return ServiceResult.Failure("Email is already registered.");
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
                return ServiceResult.Failure(result.Errors.Select(e => e.Description).ToArray());
            }

            _context.UserProfiles.Add(new UserProfile { UserId = user.Id, DateOfBirth = request.DateOfBirth });
            _context.UserPreferences.Add(new UserPreference { UserId = user.Id, TimeZone = "UTC"});
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();
            await SendConfirmationEmailAsync(user);
            return ServiceResult.Success();
        }

        public async Task<ServiceResult> ConfirmEmailAsync(int userId, string token)
        {
            var invalid = ServiceResult.Failure(new[] { "This Confirmation link is invalid or has expired." });
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
            return result.Succeeded ? ServiceResult.Success() : invalid;
        }

        public async Task<ServiceResult> ResendConfirmationAsync(string email)
        {
            // Check if email is empty or null
            if (string.IsNullOrWhiteSpace(email))
            {
                return ServiceResult.Success();
            }

            // Find the user by email
            var user = await _userManager.FindByEmailAsync(email.Trim());

            // Only send confirmation if user exists
            // and email has not been confirmed
            if (user != null && !user.EmailConfirmed)
            {
                await SendConfirmationEmailAsync(user);
            }

            return ServiceResult.Success();
        }

        private async Task SendConfirmationEmailAsync(User user)
        {
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
    }
}
