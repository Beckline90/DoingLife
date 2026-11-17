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
        private static ProfileView ToView(User user, UserProfile profile) => new()
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email!,
            Bio = profile.Bio,
            DateOfBirth = profile.DateOfBirth
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
       
            return SResult<ProfileView>.Success(ToView(user,userProfile));

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

            //  Answer with what was stored, not what was typed.
            return SResult<ProfileView>.Success(ToView(user, profile));
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
   

