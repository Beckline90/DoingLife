using System.IdentityModel.Tokens.Jwt;
using System.Text;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.Tests.Infrastructure;
using DoingLife.WebApi.ViewModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace DoingLife.WebApi.Tests.Auth;

[Collection("Database")]
public class Login_Should
{
    private const string WrongPassword = "Wrong1234!";

    private readonly DatabaseFixture _db;

    public Login_Should(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    private static ClientInfoView Make_ClientInfo(string? userAgent = null) => new()
    {
        ClientIp = "127.0.0.1",
        UserAgent = userAgent ?? "tests"
    };

    // One scope per call, like one web request
    private static async Task<SResult<AuthResponseView>> Login_Async(
        AuthTestHost host,
        string email,
        string password = AuthTestExtensions.ValidPassword,
        string? userAgent = null)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.LoginAsync(
            new LoginRequestView { Email = email, Password = password },
            Make_ClientInfo(userAgent));
    }

    private static async Task<List<SResult<AuthResponseView>>> Fail_Login_Async(
        AuthTestHost host, string email, int times)
    {
        var results = new List<SResult<AuthResponseView>>();
        for (var i = 0; i < times; i++)
        {
            results.Add(await Login_Async(host, email, WrongPassword));
        }
        return results;
    }

    private static async Task<User?> Find_User_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
    }

    private static async Task<List<UserSession>> Find_Sessions_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions
            .AsNoTracking()
            .Where(s => s.User.Email == email)
            .ToListAsync();
    }

    private static async Task<List<UserLoginHistory>> Find_History_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserLoginHistories
            .AsNoTracking()
            .Where(h => h.User.Email == email)
            .OrderBy(h => h.AttemptedAt)
            .ToListAsync();
    }

    // Attempts for an unknown email have no user, so tests find them by a unique user agent
    private static async Task<List<UserLoginHistory>> Find_History_By_UserAgent_Async(
        AuthTestHost host, string userAgent)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserLoginHistories
            .AsNoTracking()
            .Where(h => h.UserAgent == userAgent)
            .ToListAsync();
    }

    private static async Task Set_Account_State_Async(
        AuthTestHost host, string email, bool isActive, bool isDeleted)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, isActive)
                .SetProperty(u => u.IsDeleted, isDeleted));
    }

    private static async Task End_Lockout_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.LockoutEnd,
                (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1)));
    }
    #endregion

    #region valid data
    [Fact]
    public async Task Return_Tokens_And_User_Info_For_Valid_Credentials()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var result = await Login_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);
        var data = result.Data!;
        Assert.False(string.IsNullOrWhiteSpace(data.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(data.RefreshToken));
        Assert.Equal(email, data.User.Email);
        Assert.Equal("Test", data.User.FirstName);
        Assert.Equal("User", data.User.LastName);
        Assert.True(data.User.EmailConfirmed);

        // The access token lasts 15 minutes in the test settings
        Assert.InRange(
            data.AccessTokenExpireAt,
            DateTime.UtcNow.AddMinutes(14),
            DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task Create_One_Session_With_A_Hashed_Refresh_Token()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var result = await Login_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);
        var data = result.Data!;

        var sessions = await Find_Sessions_Async(host, email);
        var session = Assert.Single(sessions);

        await using var scope = host.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        Assert.Equal(64, session.RefreshTokenHash.Length);                       // a SHA-256 hash in hex
        Assert.NotEqual(data.RefreshToken, session.RefreshTokenHash);           // the plain token is never stored
        Assert.Equal(tokenService.HashRefreshToken(data.RefreshToken), session.RefreshTokenHash);
        Assert.Null(session.RevokedAt);
        Assert.InRange(
            session.ExpiresAt,
            DateTime.UtcNow.AddDays(7).AddMinutes(-1),
            DateTime.UtcNow.AddDays(7).AddMinutes(1));
    }

    [Fact]
    public async Task Create_A_Separate_Session_For_Each_Login()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var first = await Login_Async(host, email);
        var second = await Login_Async(host, email);

        //Assert
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Data!.RefreshToken, second.Data!.RefreshToken);

        var sessions = await Find_Sessions_Async(host, email);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(2, sessions.Select(s => s.RefreshTokenHash).Distinct().Count());
    }

    [Fact]
    public async Task Issue_An_Access_Token_That_Validates_And_Names_The_User()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);

        //Act
        var result = await Login_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);
        var token = result.Data!.AccessToken;

        // The same issuer, audience and key the API uses to check incoming tokens
        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = "tests",
            ValidAudience = "tests",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))),
            ClockSkew = TimeSpan.Zero
        };
        var error = Record.Exception(() => handler.ValidateToken(token, parameters, out _));
        Assert.Null(error);

        var jwt = handler.ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(email, jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.NotNull(jwt.Id);   // the unique token id
    }

    [Fact]
    public async Task Record_A_Successful_Login_In_The_History()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        await Login_Async(host, email);

        //Assert
        var history = await Find_History_Async(host, email);
        var entry = Assert.Single(history);
        Assert.True(entry.IsSuccessful);
        Assert.Null(entry.FailureReason);
        Assert.Equal("password", entry.LoginProvider);
        Assert.Equal("127.0.0.1", entry.IpAddress);
        Assert.Equal("tests", entry.UserAgent);
    }

    [Fact]
    public async Task Log_In_When_The_Email_Has_Different_Letter_Case_And_Spaces()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var result = await Login_Async(host, $"  {email.ToUpperInvariant()}  ");

        //Assert
        Assert.True(result.Succeeded);
    }
    #endregion

    #region invalid data
    [Fact]
    public async Task Return_InvalidCredentials_For_A_Wrong_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var result = await Login_Async(host, email, WrongPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
        Assert.Empty(await Find_Sessions_Async(host, email));

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal(1, user.AccessFailedCount);
    }

    [Fact]
    public async Task Return_InvalidCredentials_For_An_Unknown_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var unknown = AuthTestExtensions.NewEmail();

        //Act
        var result = await Login_Async(host, unknown);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Give_The_Same_Answer_For_An_Unknown_Email_And_A_Wrong_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var wrongPassword = await Login_Async(host, email, WrongPassword);
        var unknownEmail = await Login_Async(host, AuthTestExtensions.NewEmail());

        //Assert (nothing in the answer tells an attacker which emails exist)
        Assert.Equal(wrongPassword.ErrorCode, unknownEmail.ErrorCode);
        Assert.Equal(wrongPassword.Errors, unknownEmail.Errors);
    }

    [Theory]
    [InlineData("", AuthTestExtensions.ValidPassword)]
    [InlineData("   ", AuthTestExtensions.ValidPassword)]
    public async Task Return_InvalidCredentials_For_A_Blank_Email(string email, string password)
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Login_Async(host, email, password);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Return_InvalidCredentials_For_An_Empty_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var result = await Login_Async(host, email, "");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
        Assert.Empty(await Find_Sessions_Async(host, email));
    }

    [Fact]
    public async Task Return_EmailNotVerified_For_An_Unverified_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();   // registered, never confirmed

        //Act
        var result = await Login_Async(host, email);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.EmailNotVerified, result.ErrorCode);
        Assert.Empty(await Find_Sessions_Async(host, email));
    }

    [Fact]
    public async Task Not_Reveal_That_An_Account_Is_Unverified_When_The_Password_Is_Wrong()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();

        //Act
        var result = await Login_Async(host, email, WrongPassword);

        //Assert (the verification status is only shown to someone who knows the password)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
    }

    [Theory]
    [InlineData(false, false)]   // deactivated
    [InlineData(true, true)]     // deleted
    [InlineData(false, true)]    // both
    public async Task Return_AccountUnavailable_For_An_Inactive_Or_Deleted_Account(bool isActive, bool isDeleted)
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Set_Account_State_Async(host, email, isActive, isDeleted);

        //Act
        var result = await Login_Async(host, email);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Empty(await Find_Sessions_Async(host, email));
    }

    [Fact]
    public async Task Not_Reveal_That_An_Account_Is_Inactive_When_The_Password_Is_Wrong()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Set_Account_State_Async(host, email, isActive: false, isDeleted: false);

        //Act
        var result = await Login_Async(host, email, WrongPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.ErrorCode);
    }
    #endregion

    #region login history
    [Fact]
    public async Task Record_A_Failed_Login_For_A_Wrong_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);

        //Act
        await Login_Async(host, email, WrongPassword);

        //Assert
        var history = await Find_History_Async(host, email);
        var entry = Assert.Single(history);
        Assert.False(entry.IsSuccessful);
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal("Wrong password", entry.FailureReason);
    }

    [Fact]
    public async Task Record_A_Failed_Login_With_No_User_For_An_Unknown_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var userAgent = $"tests-{Guid.NewGuid():N}";

        //Act
        await Login_Async(host, AuthTestExtensions.NewEmail(), userAgent: userAgent);

        //Assert
        var history = await Find_History_By_UserAgent_Async(host, userAgent);
        var entry = Assert.Single(history);
        Assert.False(entry.IsSuccessful);
        Assert.Null(entry.UserId);
        Assert.Equal("Unknown email", entry.FailureReason);
    }

    [Fact]
    public async Task Record_The_Unverified_And_Unavailable_Reasons_For_Support()
    {
        //Arrange
        await using var host = Make_Host();
        var unverified = await host.RegisterUserAsync();
        var inactive = await host.RegisterConfirmedUserAsync();
        await Set_Account_State_Async(host, inactive, isActive: false, isDeleted: false);

        //Act
        await Login_Async(host, unverified);
        await Login_Async(host, inactive);

        //Assert
        var unverifiedEntry = Assert.Single(await Find_History_Async(host, unverified));
        var inactiveEntry = Assert.Single(await Find_History_Async(host, inactive));
        Assert.Equal("Email not confirmed", unverifiedEntry.FailureReason);
        Assert.Equal("Inactive or deleted account", inactiveEntry.FailureReason);
    }
    #endregion

    #region lockout
    [Fact]
    public async Task Lock_The_Account_After_Five_Wrong_Passwords()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var results = await Fail_Login_Async(host, email, 5);

        //Assert (every one of the five is answered as a plain bad password)
        Assert.All(results, r => Assert.Equal(ErrorCodes.InvalidCredentials, r.ErrorCode));

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.NotNull(user.LockoutEnd);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Refuse_The_Correct_Password_While_The_Account_Is_Locked()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Fail_Login_Async(host, email, 5);

        //Act
        var result = await Login_Async(host, email);   // the correct password

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountLocked, result.ErrorCode);
        Assert.Empty(await Find_Sessions_Async(host, email));
    }

    [Fact]
    public async Task Record_The_Locked_Attempt_In_The_History()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Fail_Login_Async(host, email, 5);

        //Act
        await Login_Async(host, email);

        //Assert
        var history = await Find_History_Async(host, email);
        Assert.Equal(6, history.Count);
        Assert.Equal("Account locked", history.Last().FailureReason);
    }

    [Fact]
    public async Task Reset_The_Failed_Count_After_A_Successful_Login()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act (four failures, a success, then four more failures: never five in a row)
        await Fail_Login_Async(host, email, 4);
        var firstSuccess = await Login_Async(host, email);
        await Fail_Login_Async(host, email, 4);
        var secondSuccess = await Login_Async(host, email);

        //Assert
        Assert.True(firstSuccess.Succeeded);
        Assert.True(secondSuccess.Succeeded);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Fact]
    public async Task Not_Lock_A_Different_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var locked = await host.RegisterConfirmedUserAsync();
        var other = await host.RegisterConfirmedUserAsync();

        //Act
        await Fail_Login_Async(host, locked, 5);
        var result = await Login_Async(host, other);

        //Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Allow_Login_Again_When_The_Lockout_Has_Ended()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Fail_Login_Async(host, email, 5);
        await End_Lockout_Async(host, email);   // the 15 minutes have passed

        //Act
        var result = await Login_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);
    }
    #endregion
}