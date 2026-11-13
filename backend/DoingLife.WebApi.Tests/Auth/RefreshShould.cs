using System.IdentityModel.Tokens.Jwt;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.Tests.Infrastructure;
using DoingLife.WebApi.ViewModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoingLife.WebApi.Tests.Auth;

[Collection("Database")]
public class Refresh_Should
{
    private readonly DatabaseFixture _db;

    public Refresh_Should(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    // One scope per call, like one web request
    private static async Task<SResult<AuthResponseView>> Refresh_Async(AuthTestHost host, string refreshToken)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.RefreshTokenAsync(refreshToken);
    }

    // Registers and confirms a user, logs in once, and returns the email and the login response
    private static async Task<(string Email, AuthResponseView Login)> Create_Logged_In_User_Async(AuthTestHost host)
    {
        var email = await host.RegisterConfirmedUserAsync();
        var login = await host.LoginAsync(email);
        Assert.True(login.Succeeded, "Test setup failed: login did not succeed.");
        return (email, login.Data!);
    }

    // A second login for the same user, like a second device
    private static async Task<AuthResponseView> Login_Again_Async(AuthTestHost host, string email)
    {
        var login = await host.LoginAsync(email);
        Assert.True(login.Succeeded, "Test setup failed: second login did not succeed.");
        return login.Data!;
    }

    private static async Task<List<UserSession>> Find_Sessions_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions
            .AsNoTracking()
            .Where(s => s.User.Email == email)
            .OrderBy(s => s.UserSessionId)
            .ToListAsync();
    }

    private static string Hash(AuthTestHost host, string refreshToken)
    {
        using var scope = host.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().HashRefreshToken(refreshToken);
    }

    private static UserSession Session_For(AuthTestHost host, List<UserSession> sessions, string refreshToken)
    {
        var hash = Hash(host, refreshToken);
        return sessions.Single(s => s.RefreshTokenHash == hash);
    }

    private static async Task Expire_Session_Async(AuthTestHost host, string refreshToken)
    {
        var hash = Hash(host, refreshToken);
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserSessions
            .Where(s => s.RefreshTokenHash == hash)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    private static async Task Set_Account_State_Async(
        AuthTestHost host, string email, bool isActive, bool isDeleted, bool emailConfirmed)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, isActive)
                .SetProperty(u => u.IsDeleted, isDeleted)
                .SetProperty(u => u.EmailConfirmed, emailConfirmed));
    }

    private static async Task Lock_Account_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.LockoutEnd,
                (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(15)));
    }
    #endregion

    #region valid data
    [Fact]
    public async Task Return_A_New_Token_Pair_For_A_Valid_Refresh_Token()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);
        var data = result.Data!;
        Assert.False(string.IsNullOrWhiteSpace(data.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(data.RefreshToken));
        Assert.NotEqual(login.RefreshToken, data.RefreshToken);
        Assert.NotEqual(login.AccessToken, data.AccessToken);
        Assert.Equal(login.User.Id, data.User.Id);
        Assert.Equal(login.User.Email, data.User.Email);
    }

    [Fact]
    public async Task Issue_An_Access_Token_For_The_Same_User()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Data!.AccessToken);
        Assert.Equal(login.User.Id.ToString(), jwt.Subject);
        Assert.InRange(
            result.Data.AccessTokenExpireAt,
            DateTime.UtcNow.AddMinutes(14),
            DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task Retire_The_Old_Session_And_Create_A_New_One()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);
        var sessions = await Find_Sessions_Async(host, email);
        Assert.Equal(2, sessions.Count);

        var oldSession = Session_For(host, sessions, login.RefreshToken);
        var newSession = Session_For(host, sessions, result.Data!.RefreshToken);

        Assert.NotNull(oldSession.RevokedAt);
        Assert.Null(newSession.RevokedAt);
    }

    [Fact]
    public async Task Store_Only_A_Hash_Of_The_New_Refresh_Token()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        var sessions = await Find_Sessions_Async(host, email);
        var newSession = Session_For(host, sessions, result.Data!.RefreshToken);

        Assert.Equal(64, newSession.RefreshTokenHash.Length);
        Assert.NotEqual(result.Data.RefreshToken, newSession.RefreshTokenHash);
        Assert.DoesNotContain(sessions, s => s.RefreshTokenHash == result.Data.RefreshToken);
    }

    [Fact]
    public async Task Give_The_New_Session_A_Fresh_Seven_Day_Expiry()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        var sessions = await Find_Sessions_Async(host, email);
        var newSession = Session_For(host, sessions, result.Data!.RefreshToken);
        Assert.InRange(
            newSession.ExpiresAt,
            DateTime.UtcNow.AddDays(7).AddMinutes(-1),
            DateTime.UtcNow.AddDays(7).AddMinutes(1));
    }

    [Fact]
    public async Task Allow_The_New_Token_To_Be_Refreshed_Again()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var first = await Refresh_Async(host, login.RefreshToken);
        var second = await Refresh_Async(host, first.Data!.RefreshToken);

        //Assert
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);

        var sessions = await Find_Sessions_Async(host, email);
        Assert.Equal(3, sessions.Count);
        Assert.Equal(2, sessions.Count(s => s.RevokedAt != null));
        Assert.Null(Session_For(host, sessions, second.Data!.RefreshToken).RevokedAt);
    }

    [Fact]
    public async Task Leave_The_Users_Other_Sessions_Alone()
    {
        //Arrange (two devices)
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        var deviceB = await Login_Again_Async(host, email);

        //Act
        var result = await Refresh_Async(host, deviceA.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);
        var sessions = await Find_Sessions_Async(host, email);
        Assert.Null(Session_For(host, sessions, deviceB.RefreshToken).RevokedAt);
    }
    #endregion

    #region reuse detection
    [Fact]
    public async Task Return_InvalidSession_When_An_Old_Token_Is_Used_Again()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);
        await Refresh_Async(host, login.RefreshToken);   // the token is now spent

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task End_Every_Session_When_A_Spent_Token_Is_Used_Again()
    {
        //Arrange (two devices, then device A refreshes normally)
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        await Login_Again_Async(host, email);
        await Refresh_Async(host, deviceA.RefreshToken);

        //Act (someone presents device A's old token again)
        await Refresh_Async(host, deviceA.RefreshToken);

        //Assert (all three sessions are dead: the two original ones and the rotated one)
        var sessions = await Find_Sessions_Async(host, email);
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions, s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task Stop_The_Newest_Token_Working_After_Reuse_Is_Detected()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);
        var rotated = await Refresh_Async(host, login.RefreshToken);
        await Refresh_Async(host, login.RefreshToken);   // reuse of the spent token

        //Act (the legitimate newest token, issued before the reuse)
        var result = await Refresh_Async(host, rotated.Data!.RefreshToken);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);
    }

    [Fact]
    public async Task Not_Touch_Another_Users_Sessions_When_Reuse_Is_Detected()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, attacked) = await Create_Logged_In_User_Async(host);
        var (otherEmail, other) = await Create_Logged_In_User_Async(host);
        await Refresh_Async(host, attacked.RefreshToken);

        //Act
        await Refresh_Async(host, attacked.RefreshToken);   // reuse

        //Assert
        var sessions = await Find_Sessions_Async(host, otherEmail);
        var session = Assert.Single(sessions);
        Assert.Null(session.RevokedAt);

        var stillWorks = await Refresh_Async(host, other.RefreshToken);
        Assert.True(stillWorks.Succeeded);
    }
    #endregion

    #region invalid data
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    [InlineData("q83JfK2L0x9Zp1AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxYyZz0123456789ABCDEFGHIJKLMNOP==")]
    public async Task Return_InvalidSession_For_A_Token_That_Does_Not_Exist(string token)
    {
        //Arrange (a real session exists, and must stay untouched)
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Refresh_Async(host, token);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);

        var sessions = await Find_Sessions_Async(host, email);
        var session = Assert.Single(sessions);
        Assert.Null(session.RevokedAt);
        Assert.Equal(Hash(host, login.RefreshToken), session.RefreshTokenHash);
    }

    [Fact]
    public async Task Return_InvalidSession_When_The_Access_Token_Is_Sent_As_A_Refresh_Token()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Refresh_Async(host, login.AccessToken);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);
    }

    [Fact]
    public async Task Return_InvalidSession_For_An_Expired_Session()
    {
        //Arrange (two devices, only device A's session is expired)
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        var deviceB = await Login_Again_Async(host, email);
        await Expire_Session_Async(host, deviceA.RefreshToken);

        //Act
        var result = await Refresh_Async(host, deviceA.RefreshToken);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);

        // An expired token is normal, so it must not be treated as theft
        var sessions = await Find_Sessions_Async(host, email);
        Assert.Null(Session_For(host, sessions, deviceB.RefreshToken).RevokedAt);

        var stillWorks = await Refresh_Async(host, deviceB.RefreshToken);
        Assert.True(stillWorks.Succeeded);
    }

    [Theory]
    [InlineData(false, false, true)]   // deactivated
    [InlineData(true, true, true)]     // deleted
    [InlineData(true, false, false)]   // email no longer confirmed
    public async Task Return_InvalidSession_When_The_Account_Is_No_Longer_Allowed_To_Sign_In(
        bool isActive, bool isDeleted, bool emailConfirmed)
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);
        await Set_Account_State_Async(host, email, isActive, isDeleted, emailConfirmed);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);

        // That session is retired, so the token can't be tried again later
        var sessions = await Find_Sessions_Async(host, email);
        var session = Assert.Single(sessions);
        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public async Task Return_InvalidSession_When_The_Account_Is_Locked_Out()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);
        await Lock_Account_Async(host, email);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);
    }

    [Fact]
    public async Task Give_The_Same_Answer_For_Every_Kind_Of_Failure()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, spent) = await Create_Logged_In_User_Async(host);
        var expired = await Login_Again_Async(host, email);
        await Refresh_Async(host, spent.RefreshToken);              // makes "spent" a used token
        await Expire_Session_Async(host, expired.RefreshToken);

        //Act
        var garbage = await Refresh_Async(host, "garbage");
        var reused = await Refresh_Async(host, spent.RefreshToken);
        var expiredResult = await Refresh_Async(host, expired.RefreshToken);

        //Assert (the answer never tells a caller why it failed)
        Assert.Equal(garbage.ErrorCode, reused.ErrorCode);
        Assert.Equal(garbage.ErrorCode, expiredResult.ErrorCode);
        Assert.Equal(garbage.Errors, reused.Errors);
        Assert.Equal(garbage.Errors, expiredResult.Errors);
    }
    #endregion

    #region concurrency
    [Fact]
    public async Task Allow_Only_One_Of_Two_Simultaneous_Refreshes_With_The_Same_Token()
    {
        //Arrange
        await using var host = Make_Host();

        //Act and Assert (repeated, because a race can pass by luck once)
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var (_, login) = await Create_Logged_In_User_Async(host);

            var results = await Task.WhenAll(
                Task.Run(() => Refresh_Async(host, login.RefreshToken)),
                Task.Run(() => Refresh_Async(host, login.RefreshToken)));

            Assert.Equal(1, results.Count(r => r.Succeeded));
        }
    }

    // DESIGN DECISION: this test records what the code does today.
    // When two requests race with one token, the loser is treated as reuse, and
    // reuse ends every session, including the new one the winner was just given.
    // So a client that fires two refreshes at once (two tabs, a retry) is logged out.
    [Fact]
    public async Task End_Every_Session_When_Two_Simultaneous_Refreshes_Race()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        await Task.WhenAll(
            Task.Run(() => Refresh_Async(host, login.RefreshToken)),
            Task.Run(() => Refresh_Async(host, login.RefreshToken)));

        //Assert
        var sessions = await Find_Sessions_Async(host, email);
        Assert.All(sessions, s => Assert.NotNull(s.RevokedAt));
    }
    #endregion
}