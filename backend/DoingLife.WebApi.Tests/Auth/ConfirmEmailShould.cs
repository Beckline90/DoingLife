using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoingLife.WebApi.Tests.Auth;

[Collection("Database")]
public class ConfirmEmail_Should
{
    private readonly DatabaseFixture _db;

    public ConfirmEmail_Should(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    // One scope per call, like one web request
    private static async Task<SResult> Confirm_Async(AuthTestHost host, int userId, string token)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.ConfirmEmailAsync(userId, token);
    }

    private static async Task<SResult> Resend_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.ResendConfirmationAsync(email);
    }

    // Reads the saved user from a fresh scope
    private static async Task<User?> Find_User_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Email == email);
    }

    private static int Emails_Sent_To(AuthTestHost host, string email)
        => host.Email.Sent.Count(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase));
    #endregion

    #region valid data
    [Fact]
    public async Task Confirm_The_Account_With_The_Token_From_The_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();
        var (userId, token) = host.Email.LastLinkFor(email);

        //Act
        var result = await Confirm_Async(host, userId, token);

        //Assert
        Assert.True(result.Succeeded);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task Put_The_Right_User_Id_In_The_Email_Link()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var email = await host.RegisterUserAsync();

        //Assert
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);

        var (userId, _) = host.Email.LastLinkFor(email);
        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task Keep_The_Account_Confirmed_When_The_Same_Link_Is_Used_Twice()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();
        var (userId, token) = host.Email.LastLinkFor(email);
        await Confirm_Async(host, userId, token);

        //Act (an Identity token stays valid until the user's security stamp changes,
        //so this does not have to fail. The important thing is the final state.)
        var exception = await Record.ExceptionAsync(() => Confirm_Async(host, userId, token));

        //Assert
        Assert.Null(exception);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
    }
    #endregion

    #region invalid data
    [Fact]
    public async Task Return_InvalidToken_When_The_Token_Was_Tampered_With()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();
        var (userId, token) = host.Email.LastLinkFor(email);
        var tampered = new string(token.Reverse().ToArray());   // same length, different content

        //Act
        var result = await Confirm_Async(host, userId, tampered);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
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
        var email = await host.RegisterUserAsync();
        var (userId, _) = host.Email.LastLinkFor(email);

        //Act
        var result = await Confirm_Async(host, userId, token);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public async Task Return_InvalidToken_When_The_User_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();
        var (userId, token) = host.Email.LastLinkFor(email);

        //Act (a real token, but for an id nobody has)
        var result = await Confirm_Async(host, userId + 100000, token);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);
    }

    [Fact]
    public async Task Return_InvalidToken_When_The_Token_Belongs_To_Another_User()
    {
        //Arrange
        await using var host = Make_Host();
        var emailA = await host.RegisterUserAsync();
        var emailB = await host.RegisterUserAsync();
        var (_, tokenA) = host.Email.LastLinkFor(emailA);
        var (idB, _) = host.Email.LastLinkFor(emailB);

        //Act (user A's token used with user B's id)
        var result = await Confirm_Async(host, idB, tokenA);

        //Assert (neither account got confirmed)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidToken, result.ErrorCode);

        var userA = await Find_User_Async(host, emailA);
        var userB = await Find_User_Async(host, emailB);
        Assert.NotNull(userA);
        Assert.NotNull(userB);
        Assert.False(userA.EmailConfirmed);
        Assert.False(userB.EmailConfirmed);
    }
    #endregion

    #region resend confirmation
    [Fact]
    public async Task Send_A_Second_Working_Email_When_Resending_For_An_Unconfirmed_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();

        //Act
        var result = await Resend_Async(host, email);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, Emails_Sent_To(host, email));

        // The newest link confirms the account
        var (userId, token) = host.Email.LastLinkFor(email);
        var confirm = await Confirm_Async(host, userId, token);
        Assert.True(confirm.Succeeded);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task Not_Send_An_Email_When_The_Account_Is_Already_Confirmed()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var sentBefore = Emails_Sent_To(host, email);

        //Act
        var result = await Resend_Async(host, email);

        //Assert (still reports success, so nobody can tell the account state apart)
        Assert.True(result.Succeeded);
        Assert.Equal(sentBefore, Emails_Sent_To(host, email));
    }

    [Fact]
    public async Task Return_Success_And_Send_Nothing_For_An_Unknown_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var unknown = AuthTestExtensions.NewEmail();

        //Act
        var result = await Resend_Async(host, unknown);

        //Assert (same answer as for a real account, so emails can't be probed)
        Assert.True(result.Succeeded);
        Assert.Empty(host.Email.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Return_Success_And_Send_Nothing_For_A_Blank_Email(string blank)
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Resend_Async(host, blank);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Empty(host.Email.Sent);
    }

    [Fact]
    public async Task Find_The_Account_When_The_Email_Has_Different_Letter_Case_And_Spaces()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();

        //Act
        var result = await Resend_Async(host, $"  {email.ToUpperInvariant()}  ");

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, Emails_Sent_To(host, email));
    }
    #endregion
}