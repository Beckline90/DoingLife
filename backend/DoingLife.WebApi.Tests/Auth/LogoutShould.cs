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
public class Logout_Should
{
    private readonly DatabaseFixture _db;

    public Logout_Should(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    // One scope per call, like one web request
    private static async Task<SResult> Logout_Async(AuthTestHost host, string refreshToken)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.LogoutAsync(refreshToken);
    }

    private static async Task<SResult> LogoutAll_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.LogoutAllAsync(userId);
    }

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

    private static async Task<UserSession?> Find_Session_By_Token_Async(AuthTestHost host, string refreshToken)
    {
        var hash = Hash(host, refreshToken);
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.RefreshTokenHash == hash);
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

    private static async Task<User?> Find_User_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
    }
    #endregion

    #region logout (one session)
    [Fact]
    public async Task Remove_The_Session_Row_For_The_Token()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Logout_Async(host, login.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Null(await Find_Session_By_Token_Async(host, login.RefreshToken));
        Assert.Empty(await Find_Sessions_Async(host, email));
    }

    [Fact]
    public async Task Return_Success_With_No_Error_Code()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Logout_Async(host, login.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Leave_The_Users_Other_Sessions_Alone()
    {
        //Arrange (two devices)
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        var deviceB = await Login_Again_Async(host, email);

        //Act
        await Logout_Async(host, deviceA.RefreshToken);

        //Assert
        var sessions = await Find_Sessions_Async(host, email);
        var remaining = Assert.Single(sessions);
        Assert.Equal(Hash(host, deviceB.RefreshToken), remaining.RefreshTokenHash);
        Assert.Null(remaining.RevokedAt);
    }

    [Fact]
    public async Task Not_Touch_Another_Users_Sessions()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, mine) = await Create_Logged_In_User_Async(host);
        var (otherEmail, other) = await Create_Logged_In_User_Async(host);

        //Act
        await Logout_Async(host, mine.RefreshToken);

        //Assert
        var sessions = await Find_Sessions_Async(host, otherEmail);
        var session = Assert.Single(sessions);
        Assert.Null(session.RevokedAt);

        var stillWorks = await Refresh_Async(host, other.RefreshToken);
        Assert.True(stillWorks.Succeeded);
    }

    [Fact]
    public async Task Make_The_Token_Useless_For_Refreshing()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);
        await Logout_Async(host, login.RefreshToken);

        //Act
        var result = await Refresh_Async(host, login.RefreshToken);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, result.ErrorCode);
    }

    // Logging out removes the row, so a stale refresh call finds nothing.
    // It is not mistaken for token theft, and the user's other devices stay signed in.
    [Fact]
    public async Task Not_Sign_Out_Other_Devices_When_A_Logged_Out_Token_Is_Used_Again()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        var deviceB = await Login_Again_Async(host, email);
        await Logout_Async(host, deviceA.RefreshToken);

        //Act (a stale client retries the refresh after logging out)
        var stale = await Refresh_Async(host, deviceA.RefreshToken);

        //Assert
        Assert.False(stale.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, stale.ErrorCode);

        var sessions = await Find_Sessions_Async(host, email);
        var remaining = Assert.Single(sessions);
        Assert.Null(remaining.RevokedAt);

        var deviceBStillWorks = await Refresh_Async(host, deviceB.RefreshToken);
        Assert.True(deviceBStillWorks.Succeeded);
    }

    [Fact]
    public async Task Succeed_When_Logging_Out_Twice()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, login) = await Create_Logged_In_User_Async(host);

        //Act
        var first = await Logout_Async(host, login.RefreshToken);
        var second = await Logout_Async(host, login.RefreshToken);

        //Assert
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task Succeed_And_Change_Nothing_For_A_Token_That_Does_Not_Exist(string token)
    {
        //Arrange (a real session exists, and must stay untouched)
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Logout_Async(host, token);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);

        var sessions = await Find_Sessions_Async(host, email);
        var session = Assert.Single(sessions);
        Assert.Equal(Hash(host, login.RefreshToken), session.RefreshTokenHash);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task Not_Delete_Anything_When_The_Access_Token_Is_Sent_Instead()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var result = await Logout_Async(host, login.AccessToken);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Single(await Find_Sessions_Async(host, email));
    }

    [Fact]
    public async Task Remove_An_Expired_Session()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);
        await Expire_Session_Async(host, login.RefreshToken);

        //Act
        var result = await Logout_Async(host, login.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Empty(await Find_Sessions_Async(host, email));
    }

    // The old token of a rotation is already retired. Logging out with it only
    // clears that retired row, and the active session carries on.
    [Fact]
    public async Task Keep_The_Active_Session_When_A_Retired_Token_Is_Used_To_Log_Out()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);
        var rotated = await Refresh_Async(host, login.RefreshToken);

        //Act
        var result = await Logout_Async(host, login.RefreshToken);

        //Assert
        Assert.True(result.Succeeded);

        var sessions = await Find_Sessions_Async(host, email);
        var active = Assert.Single(sessions);
        Assert.Null(active.RevokedAt);
        Assert.Equal(Hash(host, rotated.Data!.RefreshToken), active.RefreshTokenHash);
    }

    [Fact]
    public async Task Allow_A_New_Login_After_Logout()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);
        await Logout_Async(host, login.RefreshToken);

        //Act
        var again = await host.LoginAsync(email);

        //Assert
        Assert.True(again.Succeeded);
        var session = Assert.Single(await Find_Sessions_Async(host, email));
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task Succeed_When_Two_Logouts_Happen_At_Once()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act (two requests with the same token, like a double click)
        var results = await Task.WhenAll(
            Task.Run(() => Logout_Async(host, login.RefreshToken)),
            Task.Run(() => Logout_Async(host, login.RefreshToken)));

        //Assert
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Empty(await Find_Sessions_Async(host, email));
    }
    #endregion

    #region logout all (every session)
    [Fact]
    public async Task Revoke_Every_Session_For_The_User()
    {
        //Arrange (three devices)
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        await Login_Again_Async(host, email);
        await Login_Again_Async(host, email);

        //Act
        var result = await LogoutAll_Async(host, deviceA.User.Id);

        //Assert (the rows stay as a record, but every one is revoked)
        Assert.True(result.Succeeded);
        var sessions = await Find_Sessions_Async(host, email);
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions, s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task Stop_Every_Refresh_Token_Working()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        var deviceB = await Login_Again_Async(host, email);
        await LogoutAll_Async(host, deviceA.User.Id);

        //Act
        var refreshA = await Refresh_Async(host, deviceA.RefreshToken);
        var refreshB = await Refresh_Async(host, deviceB.RefreshToken);

        //Assert
        Assert.False(refreshA.Succeeded);
        Assert.False(refreshB.Succeeded);
        Assert.Equal(ErrorCodes.InvalidSession, refreshA.ErrorCode);
        Assert.Equal(ErrorCodes.InvalidSession, refreshB.ErrorCode);
    }

    [Fact]
    public async Task Not_Touch_Another_Users_Sessions_On_Logout_All()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, mine) = await Create_Logged_In_User_Async(host);
        var (otherEmail, other) = await Create_Logged_In_User_Async(host);

        //Act
        await LogoutAll_Async(host, mine.User.Id);

        //Assert
        var session = Assert.Single(await Find_Sessions_Async(host, otherEmail));
        Assert.Null(session.RevokedAt);

        var stillWorks = await Refresh_Async(host, other.RefreshToken);
        Assert.True(stillWorks.Succeeded);
    }

    [Fact]
    public async Task Keep_The_Original_Revoked_Time_On_Sessions_That_Were_Already_Revoked()
    {
        //Arrange (a refresh retires the first session before the logout)
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);
        await Refresh_Async(host, login.RefreshToken);

        var before = await Find_Sessions_Async(host, email);
        var revokedAtBefore = Session_For(host, before, login.RefreshToken).RevokedAt;
        Assert.NotNull(revokedAtBefore);

        //Act
        await LogoutAll_Async(host, login.User.Id);

        //Assert
        var after = await Find_Sessions_Async(host, email);
        Assert.Equal(revokedAtBefore, Session_For(host, after, login.RefreshToken).RevokedAt);
        Assert.All(after, s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task Succeed_For_A_User_With_No_Sessions()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();   // never logged in
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);

        //Act
        var result = await LogoutAll_Async(host, user.Id);

        //Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Succeed_For_A_User_That_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await LogoutAll_Async(host, 999999);

        //Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Succeed_When_Called_Twice()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        var first = await LogoutAll_Async(host, login.User.Id);
        var second = await LogoutAll_Async(host, login.User.Id);

        //Assert
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.All(await Find_Sessions_Async(host, email), s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task Allow_A_New_Login_After_Logout_All()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);
        await LogoutAll_Async(host, login.User.Id);

        //Act
        var again = await host.LoginAsync(email);

        //Assert
        Assert.True(again.Succeeded);

        var sessions = await Find_Sessions_Async(host, email);
        var newSession = Session_For(host, sessions, again.Data!.RefreshToken);
        Assert.Null(newSession.RevokedAt);

        var refresh = await Refresh_Async(host, again.Data.RefreshToken);
        Assert.True(refresh.Succeeded);
    }

    [Fact]
    public async Task Leave_The_Account_Itself_Unchanged()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, login) = await Create_Logged_In_User_Async(host);

        //Act
        await LogoutAll_Async(host, login.User.Id);

        //Assert
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.True(user.IsActive);
        Assert.False(user.IsDeleted);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.LockoutEnd);
    }
    #endregion
}