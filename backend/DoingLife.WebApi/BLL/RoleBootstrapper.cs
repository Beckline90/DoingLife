using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using Microsoft.AspNetCore.Identity;

namespace DoingLife.WebApi.BLL
{
    /// <summary>
    /// Runs at startup. Makes sure the two catalog roles exist, and gives the first
    /// SuperAdmin role to the one account named in configuration.
    /// Safe to run on every start: it changes nothing once a SuperAdmin exists.
    /// </summary>
    public class RoleBootstrapper
    {
        private readonly RoleManager<IdentityRole<int>> _roles;
        private readonly UserManager<User> _users;
        private readonly IConfiguration _config;
        private readonly ILogger<RoleBootstrapper> _logger;

        public RoleBootstrapper(
            RoleManager<IdentityRole<int>> roles, UserManager<User> users,
            IConfiguration config, ILogger<RoleBootstrapper> logger)
        {
            _roles = roles;
            _users = users;
            _config = config;
            _logger = logger;
        }

        public async Task EnsureAsync()
        {
            foreach (var name in new[] { RoleNames.SuperAdmin, RoleNames.Editor })
            {
                if (await _roles.RoleExistsAsync(name)) continue;
                var created = await _roles.CreateAsync(new IdentityRole<int>(name));
                if (!created.Succeeded)
                    throw new InvalidOperationException(
                        $"Could not create role {name}: {string.Join("; ", created.Errors.Select(e => e.Code))}");
            }

            // Only ever hand out the first SuperAdmin. Changing the setting later
            // cannot add a second one, so a leaked or edited setting is not a way in.
            if ((await _users.GetUsersInRoleAsync(RoleNames.SuperAdmin)).Count > 0) return;

            var email = _config["Bootstrap:SuperAdminEmail"];
            if (string.IsNullOrWhiteSpace(email)) return;

            var user = await _users.FindByEmailAsync(email.Trim());
            if (user == null)
            {
                _logger.LogWarning("Bootstrap SuperAdmin account is not registered yet.");
                return;
            }

            // Same rule as login: an inactive or deleted account never gets a role.
            if (!user.IsActive || user.IsDeleted)
            {
                _logger.LogWarning("Bootstrap SuperAdmin account (user {UserId}) is inactive or deleted.", user.Id);
                return;
            }

            // The account must have proven it owns the address.
            if (!await _users.IsEmailConfirmedAsync(user))
            {
                _logger.LogWarning("Bootstrap SuperAdmin account (user {UserId}) has not confirmed its email yet.", user.Id);
                return;
            }

            var result = await _users.AddToRoleAsync(user, RoleNames.SuperAdmin);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Could not assign SuperAdmin: {string.Join("; ", result.Errors.Select(e => e.Code))}");

            _logger.LogInformation("SuperAdmin role assigned to user {UserId}.", user.Id);
        }
    }
}
