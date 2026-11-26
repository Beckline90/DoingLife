using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Identity; 
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text;

namespace DoingLife.WebApi.BLL
{
    public class AccountService : IAccountService
    {
        private readonly AppDbContext _context;
        private readonly UserManager<User> _userManager; 
        private readonly ILogger<AccountService> _logger;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly ISessionService _sessionService;
        public AccountService(AppDbContext context, UserManager<User> userManager, ILogger<AccountService> logger, IConfiguration config, IEmailService emailService, ISessionService sessionService)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
            _config = config;
            _emailService = emailService;
            _sessionService = sessionService;
        }


        public async Task<SResult> ChangeEmailAsync(int userId, ChangeEmailRequestView request)
        {
            var user = await GetUser(userId);
            var password = request.CurrentPassword;
            if(!user.Succeeded)
            {
                return SResult.Failure("UserNotAvailable", ErrorCodes.AccountUnavailable);
            }


            var verifyPassword = await VerifyPasswordAsync(user.Data!, request.CurrentPassword);
            if (!verifyPassword.Succeeded)
            {
                return verifyPassword;
            }
            var email = request.NewEmail.Trim().ToLowerInvariant();
            
            if ( string.Equals(email, user.Data!.Email, StringComparison.OrdinalIgnoreCase))
            {
                return SResult.Failure("Changes not saved", ErrorCodes.EmailUnchanged);
            }
            var existingEmail = await _userManager.FindByEmailAsync(email);
            if(existingEmail != null)
            {
                return SResult.Failure("Email already in use", ErrorCodes.EmailAlreadyRegistered);
            }
            var sent = await SendEmailChangeLinkAsync(user.Data!, email);
            if (!sent)
            {
                return SResult.Failure("We couldn't send the email. Please try again.", ErrorCodes.EmailNotSent);
            }

            _logger.LogInformation("Email change link sent for user {UserId}", userId);
            return SResult.Success();

        }

        public async Task<SResult> ConfirmEmailChangeAsync(ConfirmEmailChangeRequestView request)
        {
            var invalid = SResult.Failure("This confirmation link is invalid or has expired.", ErrorCodes.InvalidToken);

            var userResult = await GetUser(request.UserId);
            if (!userResult.Succeeded)
            {
                return invalid;
            }
            var user = userResult.Data!;
            var newEmail = request.NewEmail.Trim().ToLowerInvariant();

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
            }
            catch (FormatException)
            {
                return invalid;
            }

            var oldEmail = user.Email;

            if (string.Equals(newEmail, oldEmail, StringComparison.OrdinalIgnoreCase))
            {
                return invalid;
            }
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var emailChange = await _userManager.ChangeEmailAsync(user, newEmail, decodedToken);
                if (!emailChange.Succeeded)
                {
                    return invalid;
                }

                var setUsername = await _userManager.SetUserNameAsync(user, newEmail);
                if (!setUsername.Succeeded)
                {
                    return invalid;
                }

              await _sessionService.RevokeAllSessionsAsync(user.Id);
              await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
    
                _logger.LogWarning("Email change for user {UserId} could not be saved", user.Id);
                return invalid;
            }
            if (!string.IsNullOrWhiteSpace(oldEmail))
            {
                try
                {
                    var body = $"""
                <p>Hi {WebUtility.HtmlEncode(user.FirstName)},</p>
                <p>The email address on your Doing Life account was changed on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.</p>
                <p>The new address starts with {MaskEmail(newEmail)}.</p>
                <p>If you didn't make this change, please contact support right away.</p>
                """;

                    await _emailService.SendAsync(oldEmail, "Your email address was changed", body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not send the email change notice for user {UserId}", user.Id);
                }
            }

            return SResult.Success();
        }

        public async Task<SResult> ChangePasswordAsync(int userId,ChangePasswordRequestView request)
        {
          
            var userResult = await GetUser(userId);
            if(!userResult.Succeeded)
            {
                return SResult.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }
            var user = userResult.Data!;

            var verifyPassword = await VerifyPasswordAsync(user, request.CurrentPassword);
            if(!verifyPassword.Succeeded)
            {
                return verifyPassword;
            }

            if (request.NewPassword == request.CurrentPassword)
            {
                return SResult.Failure("Choose a new password.", ErrorCodes.PasswordUnchanged);
            }
            var passwordChange = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!passwordChange.Succeeded)
            {
                if (passwordChange.Errors.Any(e => e.Code.StartsWith("Password")))
                {
                    return SResult.Failure(
                        passwordChange.Errors.Select(e => e.Description).ToArray(),
                        ErrorCodes.WeakPassword);
                }

                _logger.LogError("Password change failed for user {UserId}", user.Id);
                return SResult.Failure("Changes not saved", ErrorCodes.InvalidRequest);
            }
            await _sessionService.RevokeAllSessionsAsync(user.Id);

            var email = user.Email;
            if (!string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    var body = $"""
                <p>Hi {WebUtility.HtmlEncode(user.FirstName)},</p>
                <p>The Password on your Doing Life account was changed on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.</p>
                <p>If you didn't make this change, please contact support right away.</p>
                """;

                    await _emailService.SendAsync(email, "Your Password was changed", body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not send the password change notice for user {UserId}", user.Id);
                }
            }
            return SResult.Success();
        }

        public async Task<SResult>DeleteAccountAsync(int userId, DeleteAccountRequestView request)
        {
            var userResult = await GetUser(userId);
            if(!userResult.Succeeded)
            {
                return SResult.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }
            var user = userResult.Data!;
            var verify = await VerifyPasswordAsync(user, request.CurrentPassword);
            if (!verify.Succeeded)
            {
                return verify;
            }
 
            await using var transaction = await _context.Database.BeginTransactionAsync();

            user.IsDeleted = true;
            user.IsActive = false;
            user.DeletedAt = DateTime.UtcNow;
            user.ScheduledPurgeAt = DateTime.UtcNow.AddDays(30);
            try
            {
                var update = await _userManager.UpdateAsync(user);
                if (!update.Succeeded)
                {
                    _logger.LogError("Could not close the account for user {UserId}", user.Id);
                    return SResult.Failure("Changes not saved", ErrorCodes.AccountCloseFailed);

                }

                await _sessionService.RevokeAllSessionsAsync(user.Id);
                await transaction.CommitAsync();
            }
            catch (DbUpdateException )
            {

                _logger.LogWarning("Closing the account for user {UserId} could not be saved", user.Id);
                return SResult.Failure("Changes not saved", ErrorCodes.AccountCloseFailed);
            }
           
                 
            var email = user.Email;
            if(!string.IsNullOrWhiteSpace(email))
            {

                try
                {
                    var body = $"""
                <p>Hi {WebUtility.HtmlEncode(user.FirstName)},</p>
                <p>Your account was closed on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. It will be permanently deleted after 30 days.</p>
                <p>If you didn't make this change, please contact support right away.</p>
                """;

                    await _emailService.SendAsync(email, "Account Closure", body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not send the Account Closure notice for user {UserId}", user.Id);
                }
            }
            return SResult.Success();
        
        }

        
        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 0) return "***";
            return $"{email[0]}***{email[at..]}";
        }
       
        private async Task<SResult<User>> GetUser(int userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());

            if (user == null)
            {
                return SResult<User>.Failure("invalid Request", ErrorCodes.AccountUnavailable);
            }
            if (!user.IsActive || user.IsDeleted || !user.EmailConfirmed)
            {
                return SResult<User>.Failure("invalid Request", ErrorCodes.AccountUnavailable);
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return SResult<User>.Failure("invalid Request", ErrorCodes.AccountUnavailable);
            }

            return SResult<User>.Success(user);
        }

        private async Task<SResult> VerifyPasswordAsync(User user, string password)
        {
            var passwordCheck = await _userManager.CheckPasswordAsync(user, password);
            if (!passwordCheck)
            {
                var record = await _userManager.AccessFailedAsync(user);
                if (!record.Succeeded)
                {
                    _logger.LogWarning("Could not record a failed attempt for user {UserId}", user.Id);
                }
                var count = await _userManager.GetAccessFailedCountAsync(user);
                if (count >= 3)
                {
                    await _sessionService.RevokeAllSessionsAsync(user.Id);
                    return SResult.Failure("Invalid Session.", ErrorCodes.InvalidSession);

                }
                return SResult.Failure("Wrong Password", ErrorCodes.WrongCurrentPassword);
            }
            var reset = await _userManager.ResetAccessFailedCountAsync(user);
            if (!reset.Succeeded)
            {
                _logger.LogWarning("Could not reset the failed-attempt counter for user {UserId}", user.Id);
            }
            return SResult.Success();
        }

        // Sends the "confirm your new email address" link to the NEW address.
        // Returns false if the email could not be built or sent, so the caller can say so.
        private async Task<bool> SendEmailChangeLinkAsync(User user, string newEmail)
        {
            try
            {
                // 1. Where the link should point. Without it there is nothing to build.
                var frontendUrl = _config["App:FrontendUrl"];
                if (string.IsNullOrWhiteSpace(frontendUrl))
                {
                    _logger.LogError("App:FrontendUrl is not configured, so no email change link can be built.");
                    return false;
                }

                // 2. The token is bound to this user AND this new address,
                //    so it will not work for any other address.
                var token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);

                // 3. Make the token safe to travel inside a URL.
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                // 4. The new address contains "@" and may contain "+", so it is URL-encoded too.
                var encodedEmail = Uri.EscapeDataString(newEmail);
                var link = $"{frontendUrl.TrimEnd('/')}/confirm-email-change?userId={user.Id}&email={encodedEmail}&token={encodedToken}";

                // 5. Anything that came from a user is HTML-encoded before it goes into the message.
                var body = $"""
            <p>Hi {WebUtility.HtmlEncode(user.FirstName)},</p>
            <p>We received a request to change the email address on your Doing Life account to this address.</p>
            <p>To confirm the change, use the link below:</p>
            <p><a href="{link}">Confirm my new email</a></p>
            <p>If you didn't ask for this, you can ignore this email and nothing will change.</p>
            """;

                // 6. It goes to the NEW address: owning that inbox is the proof we need.
                await _emailService.SendAsync(newEmail, "Confirm your new email address", body);
                return true;
            }
            catch (Exception ex)
            {
                // 7. Record what happened, and let the caller know it did not work.
                _logger.LogError(ex, "Could not send the email change link for user {UserId}", user.Id);
                return false;
            }
        }
    }
}
