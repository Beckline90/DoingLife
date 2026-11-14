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
public class PasswordResetShould
{
    private const string NewPassword = "NewPass1234!";
    private const string OtherPassword = "Another1234!";
    private const string WrongPassword = "Wrong1234!";
    private const string ResetSubject = "Reset your password";

    private readonly DatabaseFixture _db;

    public PasswordResetShould(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    // One scope per call, like one web request
    private static async Task<SResult> Forgot_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.ForgotPasswordAsync(email);
    }

    private static async Task<SResult> Reset_Async(
        AuthTestHost host, int userId, string token, string newPassword)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.ResetPasswordAsync(userId, token, newPassword);
    }

    private static async Task<SResult<AuthResponseView>> Refresh_Async(AuthTestHost host, string refreshToken)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.RefreshTokenAsync(refreshToken);
    }

    private static int Reset_Emails_To(AuthTestHost host, string email)
        => host.Email.Sent.Count(m =>
            m.Subject == ResetSubject &&
            m.To.Equals(email, StringComparison.OrdinalIgnoreCase));

    // Asks for a reset, checks an email really went out, and returns the link from it
    private static async Task<(int UserId, string Token)> Request_Reset_Link_Async(AuthTestHost host, string email)
    {
        var before = Reset_Emails_To(host, email);
        var result = await Forgot_Async(host, email);

        Assert.True(result.Succeeded, "Test setup failed: forgot password did not succeed.");
        Assert.Equal(before + 1, Reset_Emails_To(host, email));

        return host.Email.LastLinkFor(email);
    }

    private static async Task<(string Email, AuthResponseView Login)> Create_Logged_In_User_Async(AuthTestHost host)
    {
        var email = await host.RegisterConfirmedUserAsync();
        var login = await host.LoginAsync(email);
        Assert.True(login.Succeeded, "Test setup failed: login did not succeed.");
        return (email, login.Data!);
    }

    private static async Task Fail_Login_Async(AuthTestHost host, string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await host.LoginAsync(email, WrongPassword);
        }
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
            .OrderBy(s => s.UserSessionId)
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
    #endregion

    #region forgot password
    [Fact]
    public async Task Send_One_Reset_Email_With_A_Front_End_Link()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var result = await Forgot_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);

        var message = Assert.Single(host.Email.Sent, m => m.Subject == ResetSubject);
        Assert.Equal(email, message.To);
        Assert.Contains("http://localhost:3000/reset-password?userId=", message.Body);
        Assert.Contains("Reset my password", message.Body);
    }

    [Fact]
    public async Task Put_The_Right_User_Id_In_The_Link()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);

        //Act
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Assert
        Assert.Equal(user.Id, userId);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Return_Success_And_Send_Nothing_For_An_Unknown_Email()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Forgot_Async(host, AuthTestExtensions.NewEmail());

        //Assert
        Assert.True(result.Succeeded);
        Assert.Empty(host.Email.Sent);
    }

    [Fact]
    public async Task Give_The_Same_Answer_For_A_Known_And_An_Unknown_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var known = await Forgot_Async(host, email);
        var unknown = await Forgot_Async(host, AuthTestExtensions.NewEmail());

        //Assert (nothing in the answer tells a caller which emails have accounts)
        Assert.Equal(known.Succeeded, unknown.Succeeded);
        Assert.Equal(known.ErrorCode, unknown.ErrorCode);
        Assert.Equal(known.Errors, unknown.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Return_Success_And_Send_Nothing_For_A_Blank_Email(string blank)
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Forgot_Async(host, blank);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Empty(host.Email.Sent);
    }

    [Theory]
    [InlineData(false, false)]   // deactivated
    [InlineData(true, true)]     // deleted
    public async Task Return_Success_And_Send_Nothing_For_An_Inactive_Or_Deleted_Account(
        bool isActive, bool isDeleted)
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Set_Account_State_Async(host, email, isActive, isDeleted);

        //Act
        var result = await Forgot_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, Reset_Emails_To(host, email));
    }

    [Fact]
    public async Task Send_A_Reset_Email_Even_When_The_Email_Was_Never_Confirmed()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();   // registered, never confirmed

        //Act
        var result = await Forgot_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal(1, Reset_Emails_To(host, email));
    }

    [Fact]
    public async Task Find_The_Account_When_The_Email_Has_Different_Letter_Case_And_Spaces()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        var result = await Forgot_Async(host, $"  {email.ToUpperInvariant()}  ");

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal(1, Reset_Emails_To(host, email));
    }

    [Fact]
    public async Task Still_Succeed_When_The_Email_Provider_Fails()
    {
        //Arrange (the account is created with the normal fake, then the sender breaks)
        await using var normalHost = Make_Host();
        var email = await normalHost.RegisterConfirmedUserAsync();

        await using var brokenHost = new AuthTestHost(
            _db.ConnectionString,
            services => services.AddSingleton<IEmailService, ThrowingEmailService>());

        //Act
        var result = await Forgot_Async(brokenHost, email);

        //Assert (the caller sees the same success as always)
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Send_A_New_Email_Each_Time_A_Reset_Is_Requested()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();

        //Act
        await Forgot_Async(host, email);
        await Forgot_Async(host, email);

        //Assert
        Assert.Equal(2, Reset_Emails_To(host, email));
    }

    [Fact]
    public async Task Change_Nothing_When_A_Reset_Is_Only_Requested()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, _) = await Create_Logged_In_User_Async(host);
        var before = await Find_User_Async(host, email);
        Assert.NotNull(before);

        //Act
        await Forgot_Async(host, email);

        //Assert (asking for a reset must not sign anyone out or change the password)
        var after = await Find_User_Async(host, email);
        Assert.NotNull(after);
        Assert.Equal(before.PasswordHash, after.PasswordHash);

        var session = Assert.Single(await Find_Sessions_Async(host, email));
        Assert.Null(session.RevokedAt);

        var login = await host.LoginAsync(email);
        Assert.True(login.Succeeded);
    }
    #endregion

    #region reset password - valid data
    [Fact]
    public async Task Set_The_New_Password_With_The_Token_From_The_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        var result = await Reset_Async(host, userId, token, NewPassword);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);

        var withNew = await host.LoginAsync(email, NewPassword);
        var withOld = await host.LoginAsync(email, AuthTestExtensions.ValidPassword);
        Assert.True(withNew.Succeeded);
        Assert.False(withOld.Succeeded);
        Assert.Equal(ErrorCodes.InvalidCredentials, withOld.ErrorCode);
    }

    [Fact]
    public async Task Store_A_Hash_And_Never_The_Plain_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        await Reset_Async(host, userId, token, NewPassword);

        //Assert
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.NotEqual(NewPassword, user.PasswordHash);
        Assert.DoesNotContain(NewPassword, user.PasswordHash);
    }

    [Fact]
    public async Task Change_The_Stored_Password_Hash()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var before = await Find_User_Async(host, email);
        Assert.NotNull(before);
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        await Reset_Async(host, userId, token, NewPassword);

        //Assert
        var after = await Find_User_Async(host, email);
        Assert.NotNull(after);
        Assert.NotEqual(before.PasswordHash, after.PasswordHash);
    }

    // The link was delivered to the inbox, so using it also proves the address is theirs
    [Fact]
    public async Task Confirm_The_Email_When_The_Account_Was_Never_Confirmed()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        var result = await Reset_Async(host, userId, token, NewPassword);

        //Assert
        Assert.True(result.Succeeded);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);

        var login = await host.LoginAsync(email, NewPassword);
        Assert.True(login.Succeeded);
    }
    #endregion

    #region reset password - single use
    [Fact]
    public async Task Return_InvalidToken_When_The_Same_Link_Is_Used_Twice()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var (userId, token) = await Request_Reset_Link_Async(host, email);
        var first = await Reset_Async(host, userId, token, NewPassword);

        //Act
        var second = await Reset_Async(host, userId, token, OtherPassword);

        //Assert (the second attempt is refused, and the first password stays)
        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, second.ErrorCode);

        Assert.True((await host.LoginAsync(email, NewPassword)).Succeeded);
        Assert.False((await host.LoginAsync(email, OtherPassword)).Succeeded);
    }

    [Fact]
    public async Task Stop_An_Older_Link_Working_When_A_Newer_One_Is_Used()
    {
        //Arrange (two reset emails are requested)
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var older = await Request_Reset_Link_Async(host, email);
        var newer = await Request_Reset_Link_Async(host, email);
        await Reset_Async(host, newer.UserId, newer.Token, NewPassword);

        //Act (the older link is tried after the newer one was used)
        var result = await Reset_Async(host, older.UserId, older.Token, OtherPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
        Assert.True((await host.LoginAsync(email, NewPassword)).Succeeded);
    }
    #endregion

    #region reset password - invalid data
    [Fact]
    public async Task Return_InvalidToken_When_The_Token_Was_Tampered_With()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var before = await Find_User_Async(host, email);
        Assert.NotNull(before);
        var (userId, token) = await Request_Reset_Link_Async(host, email);
        var tampered = new string(token.Reverse().ToArray());   // same length, different content

        //Act
        var result = await Reset_Async(host, userId, tampered, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);

        var after = await Find_User_Async(host, email);
        Assert.NotNull(after);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
    }

    // These strings can't even be decoded, so this guards the FormatException handling
    [Theory]
    [InlineData("!!!")]
    [InlineData("not base64 at all")]
    [InlineData("a")]
    public async Task Return_InvalidToken_For_A_Token_That_Is_Not_Base64(string token)
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var (userId, _) = await Request_Reset_Link_Async(host, email);

        //Act
        var result = await Reset_Async(host, userId, token, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
        Assert.True((await host.LoginAsync(email)).Succeeded);   // the old password still works
    }

    [Fact]
    public async Task Return_InvalidToken_When_The_User_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act (a real token, but for an id nobody has)
        var result = await Reset_Async(host, userId + 100000, token, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
    }

    [Fact]
    public async Task Return_InvalidToken_When_The_Token_Belongs_To_Another_User()
    {
        //Arrange
        await using var host = Make_Host();
        var emailA = await host.RegisterConfirmedUserAsync();
        var emailB = await host.RegisterConfirmedUserAsync();
        var (_, tokenA) = await Request_Reset_Link_Async(host, emailA);
        var (idB, _) = await Request_Reset_Link_Async(host, emailB);

        //Act (user A's token used with user B's id)
        var result = await Reset_Async(host, idB, tokenA, NewPassword);

        //Assert (neither password changed)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
        Assert.True((await host.LoginAsync(emailA)).Succeeded);
        Assert.True((await host.LoginAsync(emailB)).Succeeded);
    }

    [Fact]
    public async Task Return_InvalidToken_When_A_Verification_Token_Is_Used_For_A_Reset()
    {
        //Arrange (the email link sent at registration is a different kind of token)
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();
        var (userId, verificationToken) = host.Email.LastLinkFor(email);
        var before = await Find_User_Async(host, email);
        Assert.NotNull(before);

        //Act
        var result = await Reset_Async(host, userId, verificationToken, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);

        var after = await Find_User_Async(host, email);
        Assert.NotNull(after);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
    }

    [Theory]
    [InlineData(false, false)]   // deactivated after the link was sent
    [InlineData(true, true)]     // deleted after the link was sent
    public async Task Return_InvalidToken_When_The_Account_Is_Inactive_Or_Deleted(bool isActive, bool isDeleted)
    {
        //Arrange (a valid link exists, then the account is closed)
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var (userId, token) = await Request_Reset_Link_Async(host, email);
        var before = await Find_User_Async(host, email);
        Assert.NotNull(before);
        await Set_Account_State_Async(host, email, isActive, isDeleted);

        //Act
        var result = await Reset_Async(host, userId, token, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);

        var after = await Find_User_Async(host, email);
        Assert.NotNull(after);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
    }

    [Theory]
    [InlineData("short1!")]          // too short
    [InlineData("alllowercase1!")]   // no uppercase letter
    [InlineData("ALLUPPERCASE1!")]   // no lowercase letter
    [InlineData("NoDigitsHere!")]    // no digit
    [InlineData("NoSymbols123")]     // no symbol
    [InlineData("")]                 // empty
    public async Task Return_WeakPassword_And_Keep_The_Link_Working_When_The_New_Password_Breaks_The_Rules(
        string weak)
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        var result = await Reset_Async(host, userId, token, weak);

        //Assert (the password is unchanged, and the same link still works afterwards)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.WeakPassword, result.ErrorCode);
        Assert.True((await host.LoginAsync(email)).Succeeded);

        var retry = await Reset_Async(host, userId, token, NewPassword);
        Assert.True(retry.Succeeded);
    }
    #endregion

    #region effects of a successful reset
    [Fact]
    public async Task End_Every_Session_After_A_Successful_Reset()
    {
        //Arrange (two devices)
        await using var host = Make_Host();
        var (email, deviceA) = await Create_Logged_In_User_Async(host);
        var secondLogin = await host.LoginAsync(email);
        Assert.True(secondLogin.Succeeded);
        var deviceB = secondLogin.Data!;
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        await Reset_Async(host, userId, token, NewPassword);

        //Assert (the rows stay as a record, but every one is revoked)
        var sessions = await Find_Sessions_Async(host, email);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.NotNull(s.RevokedAt));

        Assert.False((await Refresh_Async(host, deviceA.RefreshToken)).Succeeded);
        Assert.False((await Refresh_Async(host, deviceB.RefreshToken)).Succeeded);
    }

    [Fact]
    public async Task Not_Touch_Another_Users_Sessions()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, _) = await Create_Logged_In_User_Async(host);
        var (otherEmail, other) = await Create_Logged_In_User_Async(host);
        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        await Reset_Async(host, userId, token, NewPassword);

        //Assert
        var session = Assert.Single(await Find_Sessions_Async(host, otherEmail));
        Assert.Null(session.RevokedAt);
        Assert.True((await Refresh_Async(host, other.RefreshToken)).Succeeded);
    }

    [Fact]
    public async Task Lift_A_Lockout_And_Let_The_User_Log_In_With_The_New_Password()
    {
        //Arrange (five wrong passwords lock the account)
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Fail_Login_Async(host, email, 5);

        var locked = await host.LoginAsync(email);
        Assert.Equal(ErrorCodes.AccountLocked, locked.ErrorCode);   // setup check

        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        var result = await Reset_Async(host, userId, token, NewPassword);

        //Assert
        Assert.True(result.Succeeded);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Null(user.LockoutEnd);
        Assert.Equal(0, user.AccessFailedCount);

        var login = await host.LoginAsync(email, NewPassword);
        Assert.True(login.Succeeded);
    }

    [Fact]
    public async Task Clear_The_Failed_Login_Count()
    {
        //Arrange (three failures, short of a lockout)
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        await Fail_Login_Async(host, email, 3);

        var before = await Find_User_Async(host, email);
        Assert.NotNull(before);
        Assert.Equal(3, before.AccessFailedCount);                  // setup check

        var (userId, token) = await Request_Reset_Link_Async(host, email);

        //Act
        await Reset_Async(host, userId, token, NewPassword);

        //Assert
        var after = await Find_User_Async(host, email);
        Assert.NotNull(after);
        Assert.Equal(0, after.AccessFailedCount);
    }
    #endregion
}