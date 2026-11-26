using System.Text;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.Tests.Infrastructure;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoingLife.WebApi.Tests.Users;

[Collection("Database")]
public class Account_Should
{
    private readonly DatabaseFixture _db;
    public Account_Should(DatabaseFixture db) => _db = db;

    private static readonly string Password = AuthTestExtensions.ValidPassword;
    private const string NewPassword = "Brand-New-Pass-9!";

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    private static List<(string To, string Subject, string Body)> Sent_Emails(AuthTestHost host, string subject) =>
        host.Email.Sent
            .Where(m => m.Subject == subject)
            .Select(m => (m.To, m.Subject, m.Body))
            .ToList();

    private static async Task<(string Email, int UserId)> Create_User_Async(AuthTestHost host)
    {
        var email = await host.RegisterConfirmedUserAsync();
        var user = await Find_User_By_Email_Async(host, email);
        Assert.NotNull(user);
        return (email, user.Id);
    }

    private static async Task<User?> Find_User_By_Email_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
    }

    private static async Task<User> Find_User_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private static async Task<bool> Password_Works_Async(AuthTestHost host, int userId, string password)
    {
        await using var scope = host.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(userId.ToString());
        return await users.CheckPasswordAsync(user!, password);
    }

    private static async Task Add_Session_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            RefreshTokenHash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> Active_Sessions_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions.CountAsync(s => s.UserId == userId && s.RevokedAt == null);
    }

    private static async Task Set_Account_State_Async(AuthTestHost host, int userId, bool isActive, bool isDeleted)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.IsActive, isActive)
            .SetProperty(u => u.IsDeleted, isDeleted));
    }

    private static async Task Lock_Account_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(
            u => u.LockoutEnd, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    // Makes the token a real change-email link would carry (encoded the same way)
    private static async Task<string> Make_Change_Token_Async(AuthTestHost host, int userId, string newEmail)
    {
        await using var scope = host.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(userId.ToString());
        var token = await users.GenerateChangeEmailTokenAsync(user!, newEmail);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    }

    private static async Task<SResult> Change_Password_Async(AuthTestHost host, int userId, string current, string next)
    {
        await using var scope = host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        return await accounts.ChangePasswordAsync(userId,
            new ChangePasswordRequestView { CurrentPassword = current, NewPassword = next });
    }

    private static async Task<SResult> Change_Email_Async(AuthTestHost host, int userId, string current, string newEmail)
    {
        await using var scope = host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        return await accounts.ChangeEmailAsync(userId,
            new ChangeEmailRequestView { CurrentPassword = current, NewEmail = newEmail });
    }

    private static async Task<SResult> Confirm_Async(AuthTestHost host, int userId, string newEmail, string token)
    {
        await using var scope = host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        return await accounts.ConfirmEmailChangeAsync(
            new ConfirmEmailChangeRequestView { UserId = userId, NewEmail = newEmail, Token = token });
    }

    private static async Task<SResult> Delete_Async(AuthTestHost host, int userId, string current)
    {
        await using var scope = host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        return await accounts.DeleteAccountAsync(userId,
            new DeleteAccountRequestView { CurrentPassword = current });
    }

    private static string New_Email() => $"new-{Guid.NewGuid():N}@example.com";
    #endregion

    #region change password - valid
    [Fact]
    public async Task Change_The_Password_So_Only_The_New_One_Works()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);

        //Act
        var result = await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        Assert.True(result.Succeeded);
        Assert.True(await Password_Works_Async(host, userId, NewPassword));
        Assert.False(await Password_Works_Async(host, userId, Password));
    }

    [Fact]
    public async Task End_Every_Session_After_A_Password_Change()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);
        await Add_Session_Async(host, userId);

        //Act
        await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        Assert.Equal(0, await Active_Sessions_Async(host, userId));
    }

    [Fact]
    public async Task Not_End_Another_Users_Sessions_When_A_Password_Changes()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, mine) = await Create_User_Async(host);
        var (_, other) = await Create_User_Async(host);
        await Add_Session_Async(host, other);

        //Act
        await Change_Password_Async(host, mine, Password, NewPassword);

        //Assert
        Assert.Equal(1, await Active_Sessions_Async(host, other));
    }

    [Fact]
    public async Task Send_A_Password_Change_Notice_To_The_Account_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_User_Async(host);

        //Act
        await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        var sent = Sent_Emails(host, "Your Password was changed");
        Assert.Single(sent);
        Assert.Equal(email, sent[0].To, ignoreCase: true);
        Assert.DoesNotContain(NewPassword, sent[0].Body);
        Assert.DoesNotContain(Password, sent[0].Body);
    }

    [Fact]
    public async Task Keep_The_Password_Change_When_The_Notice_Cannot_Be_Sent()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        host.Email.ThrowOnSend = true;   // from here on, every send fails

        //Act
        var result = await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        Assert.True(result.Succeeded);
        Assert.True(await Password_Works_Async(host, userId, NewPassword));
    }

    [Fact]
    public async Task Clear_Earlier_Failed_Attempts_After_A_Correct_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Change_Password_Async(host, userId, "wrong-1", NewPassword);
        await Change_Password_Async(host, userId, "wrong-2", NewPassword);

        //Act
        var result = await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, (await Find_User_Async(host, userId)).AccessFailedCount);
    }
    #endregion

    #region change password - refusals
    [Fact]
    public async Task Refuse_A_Wrong_Current_Password_And_Change_Nothing()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        var result = await Change_Password_Async(host, userId, "not-my-password", NewPassword);

        //Assert (a wrong password must never look like an expired session)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.WrongCurrentPassword, result.ErrorCode);
        Assert.True(await Password_Works_Async(host, userId, Password));
        Assert.Equal(1, await Active_Sessions_Async(host, userId));
    }

    [Fact]
    public async Task End_The_Sessions_On_The_Third_Wrong_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        var first = await Change_Password_Async(host, userId, "wrong-1", NewPassword);
        var second = await Change_Password_Async(host, userId, "wrong-2", NewPassword);
        Assert.Equal(1, await Active_Sessions_Async(host, userId));   // still alive after two
        var third = await Change_Password_Async(host, userId, "wrong-3", NewPassword);

        //Assert
        Assert.Equal(ErrorCodes.WrongCurrentPassword, first.ErrorCode);
        Assert.Equal(ErrorCodes.WrongCurrentPassword, second.ErrorCode);
        Assert.Equal(ErrorCodes.InvalidSession, third.ErrorCode);
        Assert.Equal(0, await Active_Sessions_Async(host, userId));
        Assert.True(await Password_Works_Async(host, userId, Password));
    }

    [Fact]
    public async Task Refuse_A_New_Password_That_Is_The_Same_As_The_Current_One()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        var result = await Change_Password_Async(host, userId, Password, Password);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.PasswordUnchanged, result.ErrorCode);
        Assert.Equal(1, await Active_Sessions_Async(host, userId));
    }

    [Theory]
    [InlineData("short1!")]
    [InlineData("alllowercase")]
    [InlineData("12345678")]
    public async Task Refuse_A_Weak_New_Password_And_Keep_The_Old_One(string weak)
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        var result = await Change_Password_Async(host, userId, Password, weak);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.WeakPassword, result.ErrorCode);
        Assert.True(await Password_Works_Async(host, userId, Password));
        Assert.Equal(1, await Active_Sessions_Async(host, userId));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Refuse_A_Password_Change_On_An_Inactive_Or_Deleted_Account(bool isActive, bool isDeleted)
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Set_Account_State_Async(host, userId, isActive, isDeleted);

        //Act
        var result = await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.True(await Password_Works_Async(host, userId, Password));
    }

    [Fact]
    public async Task Refuse_A_Password_Change_On_A_Locked_Out_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Lock_Account_Async(host, userId);

        //Act
        var result = await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Refuse_A_Password_Change_For_A_User_That_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Change_Password_Async(host, 999999, Password, NewPassword);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }
    #endregion

    #region change email - starting the change
    [Fact]
    public async Task Send_The_Link_To_The_New_Address_Only_And_Change_Nothing_Yet()
    {
        //Arrange
        await using var host = Make_Host();
        var (oldEmail, userId) = await Create_User_Async(host);
        var newEmail = New_Email();

        //Act
        var result = await Change_Email_Async(host, userId, Password, newEmail);

        //Assert
        Assert.True(result.Succeeded);
        var sent = Sent_Emails(host, "Confirm your new email address");
        Assert.Single(sent);
        Assert.Equal(newEmail, sent[0].To, ignoreCase: true);
        Assert.Contains("/confirm-email-change?", sent[0].Body);
        Assert.Contains($"userId={userId}", sent[0].Body);

        // Nothing was saved: the account still has the old address
        var user = await Find_User_Async(host, userId);
        Assert.Equal(oldEmail, user.Email, ignoreCase: true);
        Assert.Equal(oldEmail, user.UserName, ignoreCase: true);
    }

    [Fact]
    public async Task Not_Send_Anything_To_The_Old_Address_When_A_Change_Starts()
    {
        //Arrange
        await using var host = Make_Host();
        var (oldEmail, userId) = await Create_User_Async(host);

        //Act
        await Change_Email_Async(host, userId, Password, New_Email());

        //Assert
        Assert.DoesNotContain(Sent_Emails(host, "Confirm your new email address"),
            m => string.Equals(m.To, oldEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refuse_A_Wrong_Password_Before_Saying_Anything_About_The_New_Address()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        var (takenEmail, _) = await Create_User_Async(host);

        //Act (the address is taken, but the password is wrong: the password answer must win)
        var result = await Change_Email_Async(host, userId, "not-my-password", takenEmail);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.WrongCurrentPassword, result.ErrorCode);
        Assert.Empty(Sent_Emails(host, "Confirm your new email address"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refuse_The_Address_The_Account_Already_Has(bool changeCase)
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_User_Async(host);
        var sameAddress = changeCase ? email.ToUpperInvariant() : email;

        //Act
        var result = await Change_Email_Async(host, userId, Password, sameAddress);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.EmailUnchanged, result.ErrorCode);
        Assert.Empty(Sent_Emails(host, "Confirm your new email address"));
    }

    [Fact]
    public async Task Refuse_An_Address_That_Another_Account_Already_Uses()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        var (takenEmail, _) = await Create_User_Async(host);

        //Act
        var result = await Change_Email_Async(host, userId, Password, takenEmail);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, result.ErrorCode);
        Assert.Empty(Sent_Emails(host, "Confirm your new email address"));
    }

    [Fact]
    public async Task Say_So_When_The_Link_Cannot_Be_Sent()
    {
        //Arrange
        await using var host = Make_Host();
        var (oldEmail, userId) = await Create_User_Async(host);
        host.Email.ThrowOnSend = true;   // from here on, every send fails

        //Act
        var result = await Change_Email_Async(host, userId, Password, New_Email());

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.EmailNotSent, result.ErrorCode);
        Assert.Equal(oldEmail, (await Find_User_Async(host, userId)).Email, ignoreCase: true);
    }

    [Fact]
    public async Task End_The_Sessions_On_The_Third_Wrong_Password_When_Changing_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        await Change_Email_Async(host, userId, "wrong-1", New_Email());
        await Change_Email_Async(host, userId, "wrong-2", New_Email());
        var third = await Change_Email_Async(host, userId, "wrong-3", New_Email());

        //Assert
        Assert.Equal(ErrorCodes.InvalidSession, third.ErrorCode);
        Assert.Equal(0, await Active_Sessions_Async(host, userId));
    }

    [Fact]
    public async Task Refuse_An_Email_Change_On_A_Closed_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Set_Account_State_Async(host, userId, isActive: false, isDeleted: true);

        //Act
        var result = await Change_Email_Async(host, userId, Password, New_Email());

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }
    #endregion

    #region change email - confirming the link
    [Fact]
    public async Task Change_The_Email_And_The_Username_When_The_Link_Is_Confirmed()
    {
        //Arrange
        await using var host = Make_Host();
        var (oldEmail, userId) = await Create_User_Async(host);
        var newEmail = New_Email();
        var token = await Make_Change_Token_Async(host, userId, newEmail);

        //Act
        var result = await Confirm_Async(host, userId, newEmail, token);

        //Assert
        Assert.True(result.Succeeded);
        var user = await Find_User_Async(host, userId);
        Assert.Equal(newEmail, user.Email, ignoreCase: true);
        Assert.Equal(newEmail, user.UserName, ignoreCase: true);
        Assert.Equal(newEmail.ToUpperInvariant(), user.NormalizedEmail);
        Assert.Equal(newEmail.ToUpperInvariant(), user.NormalizedUserName);
        Assert.True(user.EmailConfirmed);

        // The old address no longer finds the account
        Assert.Null(await Find_User_By_Email_Async(host, oldEmail));
    }

    [Fact]
    public async Task End_Every_Session_When_The_Email_Changes()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);
        await Add_Session_Async(host, userId);
        var newEmail = New_Email();
        var token = await Make_Change_Token_Async(host, userId, newEmail);

        //Act
        await Confirm_Async(host, userId, newEmail, token);

        //Assert
        Assert.Equal(0, await Active_Sessions_Async(host, userId));
    }

    [Fact]
    public async Task Tell_The_Old_Address_But_Not_Show_The_Whole_New_One()
    {
        //Arrange
        await using var host = Make_Host();
        var (oldEmail, userId) = await Create_User_Async(host);
        var newEmail = New_Email();
        var token = await Make_Change_Token_Async(host, userId, newEmail);

        //Act
        await Confirm_Async(host, userId, newEmail, token);

        //Assert
        var sent = Sent_Emails(host, "Your email address was changed");
        Assert.Single(sent);
        Assert.Equal(oldEmail, sent[0].To, ignoreCase: true);
        Assert.DoesNotContain(newEmail, sent[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirm-email-change", sent[0].Body);   // the notice never carries an action link
    }

    [Fact]
    public async Task Keep_The_Change_When_The_Notice_Cannot_Be_Sent()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        var newEmail = New_Email();
        var token = await Make_Change_Token_Async(host, userId, newEmail);
        host.Email.ThrowOnSend = true;   // after the token is made, before confirming

        //Act
        var result = await Confirm_Async(host, userId, newEmail, token);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal(newEmail, (await Find_User_Async(host, userId)).Email, ignoreCase: true);
    }

    [Fact]
    public async Task Work_Only_Once_For_The_Same_Link()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        var newEmail = New_Email();
        var token = await Make_Change_Token_Async(host, userId, newEmail);
        await Confirm_Async(host, userId, newEmail, token);

        //Act
        var second = await Confirm_Async(host, userId, newEmail, token);

        //Assert
        Assert.False(second.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, second.ErrorCode);
    }

    [Fact]
    public async Task Refuse_A_Token_That_Was_Made_For_A_Different_Address()
    {
        //Arrange
        await using var host = Make_Host();
        var (oldEmail, userId) = await Create_User_Async(host);
        var tokenFor = New_Email();
        var token = await Make_Change_Token_Async(host, userId, tokenFor);

        //Act (the link is edited to ask for another address)
        var result = await Confirm_Async(host, userId, New_Email(), token);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
        Assert.Equal(oldEmail, (await Find_User_Async(host, userId)).Email, ignoreCase: true);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("not base64 at all")]
    [InlineData("a")]
    [InlineData("QUJDREVG")]   // decodes fine, but is not a real token
    public async Task Refuse_A_Token_That_Is_Not_Valid(string token)
    {
        //Arrange
        await using var host = Make_Host();
        var (oldEmail, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        var result = await Confirm_Async(host, userId, New_Email(), token);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
        Assert.Equal(oldEmail, (await Find_User_Async(host, userId)).Email, ignoreCase: true);
        Assert.Equal(1, await Active_Sessions_Async(host, userId));
    }

    [Fact]
    public async Task Refuse_A_Token_Used_On_Another_Users_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, owner) = await Create_User_Async(host);
        var (otherEmail, other) = await Create_User_Async(host);
        var newEmail = New_Email();
        var token = await Make_Change_Token_Async(host, owner, newEmail);

        //Act
        var result = await Confirm_Async(host, other, newEmail, token);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
        Assert.Equal(otherEmail, (await Find_User_Async(host, other)).Email, ignoreCase: true);
    }

    [Fact]
    public async Task Give_The_Same_Answer_For_Every_Kind_Of_Bad_Link()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        var (_, closedId) = await Create_User_Async(host);
        var newEmail = New_Email();
        var token = await Make_Change_Token_Async(host, closedId, newEmail);
        await Set_Account_State_Async(host, closedId, isActive: false, isDeleted: true);

        //Act
        var results = new[]
        {
            await Confirm_Async(host, 999999, newEmail, token),           // no such user
            await Confirm_Async(host, closedId, newEmail, token),         // closed account
            await Confirm_Async(host, userId, newEmail, "!!!"),           // undecodable
            await Confirm_Async(host, userId, newEmail, "QUJDREVG")       // wrong token
        };

        //Assert (nothing in the answer says which part was wrong)
        Assert.All(results, r => Assert.False(r.Succeeded));
        Assert.All(results, r => Assert.Equal(ErrorCodes.InvalidToken, r.ErrorCode));
        Assert.All(results, r => Assert.Equal(results[0].Errors, r.Errors));
    }

    [Fact]
    public async Task Let_Only_One_Account_Take_An_Address_Two_Accounts_Both_Asked_For()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, first) = await Create_User_Async(host);
        var (secondEmail, second) = await Create_User_Async(host);
        var wanted = New_Email();
        var firstToken = await Make_Change_Token_Async(host, first, wanted);
        var secondToken = await Make_Change_Token_Async(host, second, wanted);

        //Act
        var winner = await Confirm_Async(host, first, wanted, firstToken);
        var loser = await Confirm_Async(host, second, wanted, secondToken);

        //Assert
        Assert.True(winner.Succeeded);
        Assert.False(loser.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, loser.ErrorCode);
        Assert.Equal(secondEmail, (await Find_User_Async(host, second)).Email, ignoreCase: true);
    }
    #endregion

    #region delete account
    [Fact]
    public async Task Close_The_Account_Without_Removing_The_Row()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        var before = DateTime.UtcNow;

        //Act
        var result = await Delete_Async(host, userId, Password);

        //Assert
        Assert.True(result.Succeeded);
        var user = await Find_User_Async(host, userId);   // the row is still there
        Assert.True(user.IsDeleted);
        Assert.False(user.IsActive);
        Assert.NotNull(user.DeletedAt);
        Assert.NotNull(user.ScheduledPurgeAt);
        Assert.InRange(user.ScheduledPurgeAt.Value, before.AddDays(30).AddMinutes(-5), DateTime.UtcNow.AddDays(30).AddMinutes(5));
    }

    [Fact]
    public async Task Keep_The_Profile_And_Preferences_While_The_Account_Is_Waiting_To_Be_Purged()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);

        //Act
        await Delete_Async(host, userId, Password);

        //Assert
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.UserProfiles.AnyAsync(p => p.UserId == userId));
        Assert.True(await db.UserPreferences.AnyAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task End_Every_Session_When_The_Account_Is_Closed()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);
        await Add_Session_Async(host, userId);

        //Act
        await Delete_Async(host, userId, Password);

        //Assert
        Assert.Equal(0, await Active_Sessions_Async(host, userId));
    }

    [Fact]
    public async Task Not_Close_Or_Sign_Out_Another_User()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, mine) = await Create_User_Async(host);
        var (_, other) = await Create_User_Async(host);
        await Add_Session_Async(host, other);

        //Act
        await Delete_Async(host, mine, Password);

        //Assert
        var otherUser = await Find_User_Async(host, other);
        Assert.False(otherUser.IsDeleted);
        Assert.True(otherUser.IsActive);
        Assert.Equal(1, await Active_Sessions_Async(host, other));
    }

    [Fact]
    public async Task Refuse_Anything_More_Once_The_Account_Is_Closed()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Delete_Async(host, userId, Password);

        //Act
        var again = await Delete_Async(host, userId, Password);
        var password = await Change_Password_Async(host, userId, Password, NewPassword);

        //Assert
        Assert.Equal(ErrorCodes.AccountUnavailable, again.ErrorCode);
        Assert.Equal(ErrorCodes.AccountUnavailable, password.ErrorCode);
    }

    [Fact]
    public async Task Send_The_Closure_Notice_To_The_Account_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_User_Async(host);

        //Act
        await Delete_Async(host, userId, Password);

        //Assert
        var sent = Sent_Emails(host, "Account Closure");
        Assert.Single(sent);
        Assert.Equal(email, sent[0].To, ignoreCase: true);
    }

    [Fact]
    public async Task Keep_The_Account_Closed_When_The_Notice_Cannot_Be_Sent()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        host.Email.ThrowOnSend = true;   // from here on, every send fails

        //Act
        var result = await Delete_Async(host, userId, Password);

        //Assert
        Assert.True(result.Succeeded);
        Assert.True((await Find_User_Async(host, userId)).IsDeleted);
    }

    [Fact]
    public async Task Refuse_A_Wrong_Password_And_Close_Nothing()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        var result = await Delete_Async(host, userId, "not-my-password");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.WrongCurrentPassword, result.ErrorCode);
        var user = await Find_User_Async(host, userId);
        Assert.False(user.IsDeleted);
        Assert.True(user.IsActive);
        Assert.Null(user.DeletedAt);
        Assert.Null(user.ScheduledPurgeAt);
        Assert.Equal(1, await Active_Sessions_Async(host, userId));
        Assert.Empty(Sent_Emails(host, "Account Closure"));
    }

    [Fact]
    public async Task End_The_Sessions_On_The_Third_Wrong_Password_When_Deleting()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Add_Session_Async(host, userId);

        //Act
        await Delete_Async(host, userId, "wrong-1");
        await Delete_Async(host, userId, "wrong-2");
        var third = await Delete_Async(host, userId, "wrong-3");

        //Assert
        Assert.Equal(ErrorCodes.InvalidSession, third.ErrorCode);
        Assert.Equal(0, await Active_Sessions_Async(host, userId));
        Assert.False((await Find_User_Async(host, userId)).IsDeleted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Refuse_Closing_An_Inactive_Or_Deleted_Account(bool isActive, bool isDeleted)
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_User_Async(host);
        await Set_Account_State_Async(host, userId, isActive, isDeleted);

        //Act
        var result = await Delete_Async(host, userId, Password);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Refuse_Closing_A_User_That_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Delete_Async(host, 999999, Password);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }
    #endregion

    #region shape
    // A client can only send what the request models define
    [Fact]
    public void Contain_Exactly_The_Fields_Each_Account_Request_Accepts()
    {
        static string[] Names(Type t) =>
            t.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "CurrentPassword", "NewPassword" }, Names(typeof(ChangePasswordRequestView)));
        Assert.Equal(new[] { "CurrentPassword", "NewEmail" }, Names(typeof(ChangeEmailRequestView)));
        Assert.Equal(new[] { "NewEmail", "Token", "UserId" }, Names(typeof(ConfirmEmailChangeRequestView)));
        Assert.Equal(new[] { "CurrentPassword" }, Names(typeof(DeleteAccountRequestView)));
    }
    #endregion
}