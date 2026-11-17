using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.Tests.Infrastructure;
using DoingLife.WebApi.ViewModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoingLife.WebApi.Tests.Users;

[Collection("Database")]
public class Profile_Should
{
    private readonly DatabaseFixture _db;

    public Profile_Should(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    // One scope per call, like one web request
    private static async Task<SResult<ProfileView>> Get_Profile_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        return await users.GetProfileAsync(userId);
    }

    private static async Task<SResult<ProfileView>> Update_Profile_Async(
        AuthTestHost host, int userId, string firstName, string lastName, string? bio)
    {
        await using var scope = host.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        return await users.UpdateProfileAsync(userId,
            new ProfileUpdateView { FirstName = firstName, LastName = lastName, Bio = bio });
    }

    private static async Task<(string Email, int UserId)> Create_Confirmed_User_Async(AuthTestHost host)
    {
        var email = await host.RegisterConfirmedUserAsync();
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        return (email, user.Id);
    }

    private static async Task<User?> Find_User_Async(AuthTestHost host, string email)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
    }

    private static async Task<UserProfile?> Find_Profile_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId);
    }

    private static async Task Set_Bio_Async(AuthTestHost host, int userId, string? bio)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserProfiles
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Bio, bio));
    }

    private static async Task Set_Date_Of_Birth_Async(AuthTestHost host, int userId, DateOnly? date)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserProfiles
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DateOfBirth, date));
    }

    private static async Task Set_Names_Async(AuthTestHost host, int userId, string firstName, string lastName)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FirstName, firstName)
                .SetProperty(u => u.LastName, lastName));
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

    private static async Task Delete_Profile_Row_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync();
    }
    #endregion

    #region get profile - valid data
    [Fact]
    public async Task Return_The_Profile_For_An_Active_Confirmed_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        var profile = result.Data!;
        Assert.Equal(email, profile.Email);
        Assert.Equal("Test", profile.FirstName);
        Assert.Equal("User", profile.LastName);
    }

    [Fact]
    public async Task Return_No_Bio_For_A_New_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.Data!.Bio);
    }

    [Fact]
    public async Task Return_The_Saved_Bio()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Bio_Async(host, userId, "Exploring careers in nursing.");

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.Equal("Exploring careers in nursing.", result.Data!.Bio);
    }

    [Fact]
    public async Task Return_The_Date_Of_Birth()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Date_Of_Birth_Async(host, userId, new DateOnly(1995, 5, 14));

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.Equal<DateOnly?>(new DateOnly(1995, 5, 14), result.Data!.DateOfBirth);
    }

    [Fact]
    public async Task Return_No_Date_Of_Birth_When_None_Is_Saved()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Date_Of_Birth_Async(host, userId, null);

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert (an account with no date must not show 0001-01-01)
        Assert.True(result.Succeeded);
        Assert.Null(result.Data!.DateOfBirth);
    }

    [Fact]
    public async Task Return_The_Current_Names_From_The_Database()
    {
        //Arrange (the names change behind the service's back, like a support edit)
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Names_Async(host, userId, "Renamed", "Person");

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.Equal("Renamed", result.Data!.FirstName);
        Assert.Equal("Person", result.Data.LastName);
    }
    #endregion

    #region get profile - refusals
    [Theory]
    [InlineData(false, false)]   // deactivated
    [InlineData(true, true)]     // deleted
    [InlineData(false, true)]    // both
    public async Task Return_AccountUnavailable_For_An_Inactive_Or_Deleted_Account(bool isActive, bool isDeleted)
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Set_Account_State_Async(host, email, isActive, isDeleted);

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Return_AccountUnavailable_For_An_Unconfirmed_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();   // registered, never confirmed
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);

        //Act
        var result = await Get_Profile_Async(host, user.Id);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Return_AccountUnavailable_For_A_Locked_Out_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Lock_Account_Async(host, email);

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Return_AccountUnavailable_For_A_User_That_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();

        //Act (a valid token can outlive its account, so this id has no user)
        var result = await Get_Profile_Async(host, 999999);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Give_The_Same_Answer_For_Every_Kind_Of_Refusal()
    {
        //Arrange
        await using var host = Make_Host();

        var (deactivatedEmail, deactivatedId) = await Create_Confirmed_User_Async(host);
        await Set_Account_State_Async(host, deactivatedEmail, isActive: false, isDeleted: false);

        var (deletedEmail, deletedId) = await Create_Confirmed_User_Async(host);
        await Set_Account_State_Async(host, deletedEmail, isActive: true, isDeleted: true);

        var (lockedEmail, lockedId) = await Create_Confirmed_User_Async(host);
        await Lock_Account_Async(host, lockedEmail);

        var unconfirmedEmail = await host.RegisterUserAsync();
        var unconfirmed = await Find_User_Async(host, unconfirmedEmail);
        Assert.NotNull(unconfirmed);

        //Act
        var results = new[]
        {
            await Get_Profile_Async(host, deactivatedId),
            await Get_Profile_Async(host, deletedId),
            await Get_Profile_Async(host, lockedId),
            await Get_Profile_Async(host, unconfirmed.Id),
            await Get_Profile_Async(host, 999999)
        };

        //Assert (the answer never says why, so it can't be used to probe accounts)
        Assert.All(results, r => Assert.False(r.Succeeded));
        Assert.All(results, r => Assert.Equal(results[0].ErrorCode, r.ErrorCode));
        Assert.All(results, r => Assert.Equal(results[0].Errors, r.Errors));
    }

    // DESIGN DECISION: a missing profile row is a bug to find, not something to repair silently.
    [Fact]
    public async Task Refuse_And_Create_Nothing_When_The_Profile_Row_Is_Missing_On_A_Read()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Delete_Profile_Row_Async(host, userId);

        //Act
        var result = await Get_Profile_Async(host, userId);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.FailedProfile, result.ErrorCode);
        Assert.Null(result.Data);
        Assert.Null(await Find_Profile_Async(host, userId));   // nothing was quietly repaired
    }
    #endregion

    #region ownership and shape
    [Fact]
    public async Task Return_Only_The_Profile_Of_The_Id_It_Is_Given()
    {
        //Arrange
        await using var host = Make_Host();
        var (emailA, idA) = await Create_Confirmed_User_Async(host);
        var (emailB, idB) = await Create_Confirmed_User_Async(host);
        await Set_Bio_Async(host, idA, "Bio of A");
        await Set_Bio_Async(host, idB, "Bio of B");

        //Act
        var a = await Get_Profile_Async(host, idA);
        var b = await Get_Profile_Async(host, idB);

        //Assert
        Assert.Equal(emailA, a.Data!.Email);
        Assert.Equal("Bio of A", a.Data.Bio);
        Assert.Equal(emailB, b.Data!.Email);
        Assert.Equal("Bio of B", b.Data.Bio);
    }

    // The response lists exactly what the profile screen shows. If you add a field on purpose,
    // add it here too. If it appears by accident (for example a password hash), this fails.
    [Fact]
    public void Contain_Exactly_The_Fields_The_Profile_Screen_Shows()
    {
        //Arrange
        var expected = new[] { "Bio", "DateOfBirth", "Email", "FirstName", "LastName" };

        //Act
        var actual = typeof(ProfileView).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        //Assert
        Assert.Equal(expected, actual);
    }

    // The request carries only what a client may change. A client can't send what the model doesn't define.
    [Fact]
    public void Contain_Exactly_The_Fields_A_Client_May_Change()
    {
        //Arrange
        var expected = new[] { "Bio", "FirstName", "LastName" };

        //Act
        var actual = typeof(ProfileUpdateView).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        //Assert
        Assert.Equal(expected, actual);
    }
    #endregion

    #region update profile - valid data
    [Fact]
    public async Task Save_The_Names_And_Bio_And_Return_The_Updated_Profile()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Profile_Async(host, userId, "Mary-Jane", "O'Brien", "I want to study in Alberta.");

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal("Mary-Jane", result.Data!.FirstName);
        Assert.Equal("O'Brien", result.Data.LastName);
        Assert.Equal("I want to study in Alberta.", result.Data.Bio);
        Assert.Equal(email, result.Data.Email);

        var user = await Find_User_Async(host, email);
        var profile = await Find_Profile_Async(host, userId);
        Assert.NotNull(user);
        Assert.NotNull(profile);
        Assert.Equal("Mary-Jane", user.FirstName);
        Assert.Equal("O'Brien", user.LastName);
        Assert.Equal("I want to study in Alberta.", profile.Bio);
    }

    [Fact]
    public async Task Trim_The_Names_And_Bio_Before_Saving()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Profile_Async(host, userId, "  Anna  ", "  Smith ", "   hello there   ");

        //Assert
        Assert.Equal("Anna", result.Data!.FirstName);
        Assert.Equal("Smith", result.Data.LastName);
        Assert.Equal("hello there", result.Data.Bio);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal("Anna", user.FirstName);
        Assert.Equal("Smith", user.LastName);
        Assert.Equal("hello there", (await Find_Profile_Async(host, userId))!.Bio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Clear_The_Bio_When_The_New_Bio_Is_Blank(string? blank)
    {
        //Arrange (a bio exists, so clearing it is visible)
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Bio_Async(host, userId, "old bio");

        //Act
        var result = await Update_Profile_Async(host, userId, "Test", "User", blank);

        //Assert (blank means no bio, and spaces are never stored as a bio)
        Assert.True(result.Succeeded);
        Assert.Null(result.Data!.Bio);
        Assert.Null((await Find_Profile_Async(host, userId))!.Bio);
    }

    // DESIGN DECISION: one full-replacement update. The client always sends the whole profile,
    // so a request that carries no bio clears it. If you ever switch to partial updates, change this test.
    [Fact]
    public async Task Clear_The_Bio_When_An_Update_Is_Sent_Without_One()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Bio_Async(host, userId, "bio that was not sent back");

        //Act
        var result = await Update_Profile_Async(host, userId, "Anna", "Smith", null);

        //Assert
        Assert.Equal("Anna", result.Data!.FirstName);
        Assert.Null(result.Data.Bio);
        Assert.Null((await Find_Profile_Async(host, userId))!.Bio);
    }

    [Fact]
    public async Task Return_The_Same_Profile_A_Later_Read_Returns()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var updated = await Update_Profile_Async(host, userId, "Anna", "Smith", "my bio");
        var read = await Get_Profile_Async(host, userId);

        //Assert (the client sees exactly what was saved)
        Assert.Equal(read.Data!.FirstName, updated.Data!.FirstName);
        Assert.Equal(read.Data.LastName, updated.Data.LastName);
        Assert.Equal(read.Data.Bio, updated.Data.Bio);
        Assert.Equal(read.Data.Email, updated.Data.Email);
        Assert.Equal(read.Data.DateOfBirth, updated.Data.DateOfBirth);
    }

    [Fact]
    public async Task Not_Change_Another_Users_Profile()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, mine) = await Create_Confirmed_User_Async(host);
        var (otherEmail, other) = await Create_Confirmed_User_Async(host);
        await Set_Bio_Async(host, other, "other person's bio");

        //Act
        await Update_Profile_Async(host, mine, "Changed", "Name", "my new bio");

        //Assert
        var otherUser = await Find_User_Async(host, otherEmail);
        Assert.NotNull(otherUser);
        Assert.Equal("Test", otherUser.FirstName);
        Assert.Equal("User", otherUser.LastName);
        Assert.Equal("other person's bio", (await Find_Profile_Async(host, other))!.Bio);
        Assert.Equal("my new bio", (await Find_Profile_Async(host, mine))!.Bio);
    }

    // DESIGN DECISION: there is no 30-day limit. This test records that.
    [Fact]
    public async Task Allow_Changing_The_Profile_Again_Straight_Away()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        var first = await Update_Profile_Async(host, userId, "Anna", "Smith", "one");

        //Act
        var second = await Update_Profile_Async(host, userId, "Ben", "Jones", "two");

        //Assert
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal("Ben", second.Data!.FirstName);
        Assert.Equal("two", second.Data.Bio);
    }

    [Fact]
    public async Task Leave_The_Email_Date_Of_Birth_And_Account_Flags_Unchanged()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Set_Date_Of_Birth_Async(host, userId, new DateOnly(1995, 5, 14));
        var before = await Find_User_Async(host, email);
        Assert.NotNull(before);

        //Act
        await Update_Profile_Async(host, userId, "Anna", "Smith", "new bio");

        //Assert
        var after = await Find_User_Async(host, email);
        Assert.NotNull(after);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.EmailConfirmed, after.EmailConfirmed);
        Assert.Equal(before.IsActive, after.IsActive);
        Assert.Equal(before.IsDeleted, after.IsDeleted);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal<DateOnly?>(new DateOnly(1995, 5, 14), (await Find_Profile_Async(host, userId))!.DateOfBirth);
    }

    [Fact]
    public async Task Keep_The_Users_Sessions_Alive_After_An_Update()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterConfirmedUserAsync();
        var login = await host.LoginAsync(email);
        Assert.True(login.Succeeded);
        var userId = login.Data!.User.Id;

        //Act
        await Update_Profile_Async(host, userId, "Anna", "Smith", "bio");

        //Assert (a name or a bio is not a credential, so nobody is signed out)
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var refresh = await auth.RefreshTokenAsync(login.Data.RefreshToken);
        Assert.True(refresh.Succeeded);
    }

    [Fact]
    public async Task Stamp_The_Profile_With_The_Current_Utc_Time_When_Updating()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        var before = DateTime.UtcNow;

        //Act
        var result = await Update_Profile_Async(host, userId, "Anna", "Smith", "bio");
        var after = DateTime.UtcNow;

        //Assert (a local-time stamp would be hours off on a machine that isn't on UTC)
        Assert.True(result.Succeeded);
        var profile = await Find_Profile_Async(host, userId);
        Assert.NotNull(profile);
        Assert.InRange(profile.UpdatedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }
    #endregion

    #region update profile - refusals
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Refuse_An_Update_On_An_Inactive_Or_Deleted_Account_And_Change_Nothing(
        bool isActive, bool isDeleted)
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Set_Bio_Async(host, userId, "original bio");
        await Set_Account_State_Async(host, email, isActive, isDeleted);

        //Act
        var result = await Update_Profile_Async(host, userId, "Changed", "Name", "attempted bio");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.LastName);
        Assert.Equal("original bio", (await Find_Profile_Async(host, userId))!.Bio);
    }

    [Fact]
    public async Task Refuse_An_Update_On_A_Locked_Out_Account_And_Change_Nothing()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Lock_Account_Async(host, email);

        //Act
        var result = await Update_Profile_Async(host, userId, "Changed", "Name", "attempted bio");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal("Test", user.FirstName);
    }

    [Fact]
    public async Task Refuse_An_Update_For_A_User_That_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Update_Profile_Async(host, 999999, "Anna", "Smith", "bio");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Null(result.Data);
    }

    // DESIGN DECISION: a missing profile row is a bug to find, not something to repair silently.
    [Fact]
    public async Task Refuse_An_Update_And_Change_Nothing_When_The_Profile_Row_Is_Missing()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Delete_Profile_Row_Async(host, userId);

        //Act
        var result = await Update_Profile_Async(host, userId, "Changed", "Name", "new bio");

        //Assert (the names on the user row must not change either, because the update is all or nothing)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.FailedProfile, result.ErrorCode);
        Assert.Null(await Find_Profile_Async(host, userId));

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.LastName);
    }

    [Theory]
    [InlineData("", "User")]
    [InlineData("   ", "User")]
    [InlineData("Test", "")]
    [InlineData("Test", "   ")]
    public async Task Refuse_And_Change_Nothing_When_A_Name_Is_Blank(string firstName, string lastName)
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Set_Bio_Async(host, userId, "original bio");

        //Act
        var result = await Update_Profile_Async(host, userId, firstName, lastName, "attempted bio");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidName, result.ErrorCode);

        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.LastName);
        Assert.Equal("original bio", (await Find_Profile_Async(host, userId))!.Bio);
    }
    #endregion

    #region update profile - races
    // DESIGN DECISION: last write wins. The names live on one row, so the first and last name
    // must always come from the same request. (The bio is on another table, so it may come from
    // either request, and that is accepted.) A failed result is fine, an unhandled exception is not.
    [Fact]
    public async Task Never_Mix_The_First_And_Last_Name_Of_Two_Racing_Updates()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);

        //Act (an unhandled exception from either call would fail the test here)
        await Task.WhenAll(
            Task.Run(() => Update_Profile_Async(host, userId, "Anna", "Smith", "first bio")),
            Task.Run(() => Update_Profile_Async(host, userId, "Ben", "Jones", "second bio")));

        //Assert
        var user = await Find_User_Async(host, email);
        var profile = await Find_Profile_Async(host, userId);
        Assert.NotNull(user);
        Assert.NotNull(profile);

        var pair = (user.FirstName, user.LastName);
        Assert.True(pair == ("Anna", "Smith") || pair == ("Ben", "Jones"),
            $"Names were mixed: {pair.FirstName} {pair.LastName}");
        Assert.Contains(profile.Bio, new[] { "first bio", "second bio" });
    }
    #endregion
}