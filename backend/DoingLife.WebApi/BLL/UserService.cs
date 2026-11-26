using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;



namespace DoingLife.WebApi.BLL
{
    public class UserService:IUserService
    {
        private readonly AppDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<UserService> _logger;
       

        public UserService(AppDbContext context, UserManager<User> userManager, ILogger<UserService> logger)
        {
         _context = context;
         _userManager = userManager;
          _logger = logger;
           

        }
        #region --- Map entity to ViewModel ---
        private static ProfileView ToProfileView(User user, UserProfile profile) => new()
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email!,
            Bio = profile.Bio,
            DateOfBirth = profile.DateOfBirth
        };

        private static UserPreferenceView ToPreferenceView(UserPreference preference) => new()
        {
         PreferredLanguage = preference.PreferredLanguage ?? "en",
         TimeZone = preference.TimeZone,
         NotificationsEnabled = preference.NotificationsEnabled,
         MemoryEnabled = preference.MemoryEnabled,
         MemoryChoiceMade = preference.MemoryChoiceMadeAt!= null

        };

        #endregion

        public async Task<SResult<ProfileView>>GetProfileAsync(int userId)
        {
            var result = await GetUserAsync(userId);
            if(!result.Succeeded)
            {
                return SResult<ProfileView>.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }
            var user = result.Data!;
            var userProfile = await _context.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user.Id);
            if (userProfile == null)
            {
                _logger.LogError("User {UserId} has no profile row. Registration or a data change left the account incomplete.",user.Id);
                return SResult<ProfileView>.Failure("Invalid Request", ErrorCodes.FailedProfile);
            }
       
            return SResult<ProfileView>.Success(ToProfileView(user,userProfile));

        }
        public async Task<SResult<ProfileView>>UpdateProfileAsync(int userId, ProfileUpdateView request)
        {
            var result = await GetUserAsync(userId);
            if (!result.Succeeded)
            {
                return SResult<ProfileView>.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }
            var user = result.Data!;
            var profile = await _context.UserProfiles.AsTracking().SingleOrDefaultAsync(u => u.UserId == user.Id);
            if(profile == null)
            {
                _logger.LogError("User {UserId} has no profile row. Registration or a data change left the account incomplete.", user.Id);
                return SResult<ProfileView>.Failure("Invalid Request", ErrorCodes.FailedProfile);
            }
         
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            {
                return SResult<ProfileView>.Failure("First and last name are required.", ErrorCodes.InvalidName);
            }
            var bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();

            user.FirstName = request.FirstName.Trim();
            user.LastName = request.LastName.Trim();
            profile.Bio = bio;
            profile.UpdatedAt = DateTime.UtcNow;

            // One save. Both rows are tracked by the same context, so this is one transaction:
            // both changes are kept, or neither is.
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogWarning("Profile update for user {UserId} collided with another change.", user.Id);
                return SResult<ProfileView>.Failure(
                    "The profile changed while saving. Please reload and try again.", ErrorCodes.Conflict);
            }

            //  Answer with what was stored, not what was typed.a
            return SResult<ProfileView>.Success(ToProfileView(user, profile));
        }
        
        public async Task<SResult<UserPreferenceView>> GetPreferencesAsync(int userId)
        {
            var result = await GetUserAsync(userId);
            if(!result.Succeeded)
            {
                return SResult<UserPreferenceView>.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }        
            var preference = await _context.UserPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId);

            if(preference == null)
            {
                _logger.LogError("User {UserId} has no preference row. Registration or a data change left the account incomplete.",userId);
                return SResult<UserPreferenceView>.Failure("Missing Preference", ErrorCodes.MissingPreferences);
                
            }
            return SResult<UserPreferenceView>.Success(ToPreferenceView(preference));

        }

        public async Task<SResult<UserPreferenceView>> UpdatePreferencesAsync(int userId, PreferenceUpdateView request)
        {
            var userResult = await GetUserAsync(userId);
            if(!userResult.Succeeded)
            {
                return SResult<UserPreferenceView>.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }
            var preference = await _context.UserPreferences.AsTracking().SingleOrDefaultAsync(p => p.UserId == userId);
            if(preference == null)
            {
                _logger.LogError("User {UserId} has no preference row. Registration or a data change left the account incomplete.", userId);
                return SResult<UserPreferenceView>.Failure("Missing Preference", ErrorCodes.MissingPreferences);
            }
            // Language: only a value from the fixed list, stored in lowercase
            string? language = null;
            if (!string.IsNullOrWhiteSpace(request.PreferredLanguage))
            {
                language = request.PreferredLanguage.Trim().ToLowerInvariant();
                if (!AppRules.SupportedLanguages.Contains(language))
                {
                    return SResult<UserPreferenceView>.Failure(
                        "Preferred language is not supported.", ErrorCodes.InvalidLanguage);
                }
            }

           
            string? timeZone = null;
            if (!string.IsNullOrWhiteSpace(request.TimeZone))
            {
                timeZone = request.TimeZone.Trim();
                if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
                {
                    return SResult<UserPreferenceView>.Failure(
                        "Time zone is not recognised.", ErrorCodes.InvalidTimeZone);
                }
            }

            var anythingSent = language != null
                              || timeZone != null
                              || request.NotificationsEnabled != null
                              || request.MemoryEnabled != null;
            if(!anythingSent)
            {
                return SResult<UserPreferenceView>.Success(ToPreferenceView(preference));
            }
                             
            if(language != null) preference.PreferredLanguage = language;
            if(timeZone != null) preference.TimeZone = timeZone;
            if(request.NotificationsEnabled != null)preference.NotificationsEnabled = request.NotificationsEnabled.Value;
            if (request.MemoryEnabled != null)
            {
                preference.MemoryEnabled = request.MemoryEnabled.Value;
                preference.MemoryChoiceMadeAt = DateTime.UtcNow;  
            }
            ;
            preference.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogWarning("Preference update for user {UserId} collided with another change.", userId);
                return SResult<UserPreferenceView>.Failure(
                    "The preferences changed while saving. Please reload and try again.", ErrorCodes.Conflict);
            }

            return SResult<UserPreferenceView>.Success(ToPreferenceView(preference));

        }
       

        private async Task<SResult<User>> GetUserAsync(int userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            
            if(user == null)
            {
                return SResult<User>.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }
            if(!user.IsActive || !user.EmailConfirmed || user.IsDeleted)
            {
                return SResult<User>.Failure("Invalid Request", ErrorCodes.AccountUnavailable);
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return SResult<User>.Failure("Invalid Request",ErrorCodes.AccountUnavailable);
            }
           
            
            return SResult<User>.Success(user);
        }

    }

}
   

