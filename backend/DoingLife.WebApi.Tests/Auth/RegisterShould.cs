using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.Tests.Infrastructure;
using DoingLife.WebApi.ViewModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DoingLife.WebApi.Tests.Infrastructure.FakeEmailService;

namespace DoingLife.WebApi.Tests.Auth;

[Collection("Database")]
public class Register_Should
{
    private readonly DatabaseFixture _db;

    public Register_Should(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host(Action<IServiceCollection>? configure = null)
        => new AuthTestHost(_db.ConnectionString, configure);

    // One scope per call, like one web request
    private static async Task<SResult> Register_Async(AuthTestHost host, RegisterRequestView request)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.RegisterAsync(request);
    }

    // Reads the saved user from a fresh scope, with profile and preferences
    private static async Task<User?> Find_User_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .AsNoTracking()
            .Include(u => u.UserProfile)
            .Include(u => u.UserPreference)
            .SingleOrDefaultAsync(u => u.Email == email);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
    #endregion

    #region valid data
    [Fact]
    public async Task Create_User_Profile_And_Preferences_With_Valid_Request()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest();

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.True(result.Succeeded);

        var user = await Find_User_Async(host, request.Email);
        Assert.NotNull(user);
        Assert.NotNull(user.UserProfile);
        Assert.NotNull(user.UserPreference);
        Assert.True(user.IsActive);
        Assert.False(user.IsDeleted);
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public async Task Store_A_Hash_And_Never_The_Plain_Password()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest();

        //Act
        await Register_Async(host, request);

        //Assert
        var user = await Find_User_Async(host, request.Email);
        Assert.NotNull(user);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.NotEqual(request.Password, user.PasswordHash);
        Assert.DoesNotContain(request.Password, user.PasswordHash);
    }

    [Fact]
    public async Task Store_Email_Trimmed_And_Lowercase()
    {
        //Arrange
        var email = AuthTestExtensions.NewEmail();   // already lowercase
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(email: $"  {email.ToUpperInvariant()}  ");

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.True(result.Succeeded);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal(email, user.UserName);
        Assert.Single(host.Email.Sent, m => m.To == email);
    }

    [Fact]
    public async Task Store_First_And_Last_Name_Trimmed()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest();
        request.FirstName = "  Mary-Jane  ";
        request.LastName = "  O'Brien ";

        //Act
        await Register_Async(host, request);

        //Assert
        var user = await Find_User_Async(host, request.Email);
        Assert.NotNull(user);
        Assert.Equal("Mary-Jane", user.FirstName);
        Assert.Equal("O'Brien", user.LastName);
    }

    [Fact]
    public async Task Send_One_Verification_Email_With_A_Front_End_Link()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest();

        //Act
        await Register_Async(host, request);

        //Assert
        var user = await Find_User_Async(host, request.Email);
        Assert.NotNull(user);

        var message = Assert.Single(host.Email.Sent);
        Assert.Equal(request.Email, message.To);
        Assert.Contains("http://localhost:3000/verify-email?userId=", message.Body);

        var (userId, token) = host.Email.LastLinkFor(request.Email);
        Assert.Equal(user.Id, userId);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Html_Encode_The_First_Name_In_The_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest();
        request.FirstName = "Tom & <Jerry>";

        //Act
        await Register_Async(host, request);

        //Assert
        var message = Assert.Single(host.Email.Sent);
        Assert.Contains("Tom &amp; &lt;Jerry&gt;", message.Body);
        Assert.DoesNotContain("<Jerry>", message.Body);
    }

    [Fact]
    public async Task Still_Succeed_When_The_Email_Provider_Fails()
    {
        //Arrange (replace the email sender with one that always throws)
        await using var host = Make_Host(services =>
            services.AddSingleton<IEmailService, ThrowingEmailService>());
        var request = AuthTestExtensions.NewRegisterRequest();

        //Act
        var result = await Register_Async(host, request);

        //Assert (the account exists even though no email could be sent)
        Assert.True(result.Succeeded);
        Assert.NotNull(await Find_User_Async(host, request.Email));
    }
    #endregion

    #region age boundaries
    [Fact]
    public async Task Accept_A_User_Who_Turns_18_Today()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(dateOfBirth: Today().AddYears(-18));

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(await Find_User_Async(host, request.Email));
    }

    [Fact]
    public async Task Return_UnderAge_When_User_Turns_18_Tomorrow()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(
            dateOfBirth: Today().AddYears(-18).AddDays(1));

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.UnderAge, result.ErrorCode);
        Assert.Null(await Find_User_Async(host, request.Email));
        Assert.Empty(host.Email.Sent);
    }
    #endregion

    #region invalid data
    [Fact]
    public async Task Return_UnderAge_When_User_Is_Under_18()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(dateOfBirth: Today().AddYears(-17));

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.UnderAge, result.ErrorCode);
        Assert.Null(await Find_User_Async(host, request.Email));
        Assert.Empty(host.Email.Sent);
    }

    [Fact]
    public async Task Return_InvalidDateOfBirth_When_Date_Is_In_The_Future()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(dateOfBirth: Today().AddDays(1));

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidDateOfBirth, result.ErrorCode);
        Assert.Null(await Find_User_Async(host, request.Email));
    }

    [Fact]
    public async Task Return_InvalidDateOfBirth_When_User_Is_Older_Than_The_Maximum_Age()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(
            dateOfBirth: Today().AddYears(-(AppRules.MaxAge + 1)));

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidDateOfBirth, result.ErrorCode);
        Assert.Null(await Find_User_Async(host, request.Email));
    }

    [Fact]
    public async Task Return_TermsNotAccepted_When_Terms_Are_Not_Accepted()
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(acceptedTerms: false);

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.TermsNotAccepted, result.ErrorCode);
        Assert.Null(await Find_User_Async(host, request.Email));
        Assert.Empty(host.Email.Sent);
    }

    [Fact]
    public async Task Return_EmailAlreadyRegistered_For_A_Duplicate_Email()
    {
        //Arrange
        await using var host = Make_Host();
        var email = AuthTestExtensions.NewEmail();
        var first = await Register_Async(host, AuthTestExtensions.NewRegisterRequest(email: email));

        //Act
        var second = await Register_Async(host, AuthTestExtensions.NewRegisterRequest(email: email));

        //Assert
        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, second.ErrorCode);
        Assert.Single(host.Email.Sent);   // only the first registration sent an email
    }

    [Fact]
    public async Task Return_EmailAlreadyRegistered_When_Only_The_Letter_Case_Differs()
    {
        //Arrange
        await using var host = Make_Host();
        var email = AuthTestExtensions.NewEmail();
        await Register_Async(host, AuthTestExtensions.NewRegisterRequest(email: email));

        //Act
        var second = await Register_Async(
            host, AuthTestExtensions.NewRegisterRequest(email: email.ToUpperInvariant()));

        //Assert
        Assert.False(second.Succeeded);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, second.ErrorCode);
    }

    // A [Theory] runs once for each [InlineData] value
    [Theory]
    [InlineData("short1!")]          // too short
    [InlineData("alllowercase1!")]   // no uppercase letter
    [InlineData("ALLUPPERCASE1!")]   // no lowercase letter
    [InlineData("NoDigitsHere!")]    // no digit
    [InlineData("NoSymbols123")]     // no symbol
    public async Task Return_WeakPassword_When_Password_Breaks_The_Rules(string password)
    {
        //Arrange
        await using var host = Make_Host();
        var request = AuthTestExtensions.NewRegisterRequest(password: password);

        //Act
        var result = await Register_Async(host, request);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.WeakPassword, result.ErrorCode);
        Assert.Null(await Find_User_Async(host, request.Email));
        Assert.Empty(host.Email.Sent);
    }
    #endregion
}