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
public class Preferences_Should
{
    private readonly DatabaseFixture _db;

    public Preferences_Should(DatabaseFixture db) => _db = db;

    #region support methods
    private AuthTestHost Make_Host() => new AuthTestHost(_db.ConnectionString);

    // One scope per call, like one web request
    private static async Task<SResult<UserPreferenceView>> Get_Preferences_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        return await users.GetPreferencesAsync(userId);
    }

    // Anything left as null is "not sent"
    private static async Task<SResult<UserPreferenceView>> Update_Preferences_Async(
        AuthTestHost host,
        int userId,
        string? language = null,
        string? timeZone = null,
        bool? notifications = null,
        bool? memory = null)
    {
        await using var scope = host.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        return await users.UpdatePreferencesAsync(userId, new PreferenceUpdateView
        {
            PreferredLanguage = language,
            TimeZone = timeZone,
            NotificationsEnabled = notifications,
            MemoryEnabled = memory
        });
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

    private static async Task<UserPreference?> Find_Preference_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId);
    }

    private static async Task Set_Language_Async(AuthTestHost host, int userId, string? language)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserPreferences
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PreferredLanguage, language));
    }

    private static async Task Set_Memory_Choice_Time_Async(AuthTestHost host, int userId, DateTime? at)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserPreferences
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.MemoryChoiceMadeAt, at));
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

    private static async Task Delete_Preference_Row_Async(AuthTestHost host, int userId)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync();
    }
    #endregion

    #region get preferences - valid data
    [Fact]
    public async Task Return_The_Starting_Preferences_For_A_New_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Get_Preferences_Async(host, userId);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        var view = result.Data!;
        Assert.Equal("en", view.PreferredLanguage);
        Assert.Equal("UTC", view.TimeZone);
        Assert.True(view.NotificationsEnabled);
        Assert.True(view.MemoryEnabled);
        Assert.False(view.MemoryChoiceMade);   // nobody has chosen yet
    }

    [Fact]
    public async Task Return_The_Stored_Language()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Language_Async(host, userId, "fr");

        //Act
        var result = await Get_Preferences_Async(host, userId);

        //Assert (a mapper that always says "en" would fail here)
        Assert.Equal("fr", result.Data!.PreferredLanguage);
    }

    [Fact]
    public async Task Return_English_When_The_Stored_Language_Is_Null()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Language_Async(host, userId, null);

        //Act
        var result = await Get_Preferences_Async(host, userId);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal("en", result.Data!.PreferredLanguage);
    }

    // A read must never write. The English fallback is only for display.
    [Fact]
    public async Task Not_Write_Anything_When_Reading()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Language_Async(host, userId, null);
        var before = await Find_Preference_Async(host, userId);
        Assert.NotNull(before);

        //Act
        await Get_Preferences_Async(host, userId);

        //Assert
        var after = await Find_Preference_Async(host, userId);
        Assert.NotNull(after);
        Assert.Null(after.PreferredLanguage);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.MemoryChoiceMadeAt, after.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Say_The_Memory_Choice_Was_Made_When_The_Timestamp_Is_Set()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Memory_Choice_Time_Async(host, userId, DateTime.UtcNow);

        //Act
        var result = await Get_Preferences_Async(host, userId);

        //Assert
        Assert.True(result.Data!.MemoryChoiceMade);
    }

    [Fact]
    public async Task Return_Each_User_Only_Their_Own_Preferences()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, idA) = await Create_Confirmed_User_Async(host);
        var (_, idB) = await Create_Confirmed_User_Async(host);
        await Set_Language_Async(host, idA, "fr");
        await Set_Memory_Choice_Time_Async(host, idB, DateTime.UtcNow);

        //Act
        var a = await Get_Preferences_Async(host, idA);
        var b = await Get_Preferences_Async(host, idB);

        //Assert
        Assert.Equal("fr", a.Data!.PreferredLanguage);
        Assert.False(a.Data.MemoryChoiceMade);
        Assert.Equal("en", b.Data!.PreferredLanguage);
        Assert.True(b.Data.MemoryChoiceMade);
    }
    #endregion

    #region get preferences - refusals
    [Theory]
    [InlineData(false, false)]   // deactivated
    [InlineData(true, true)]     // deleted
    [InlineData(false, true)]    // both
    public async Task Refuse_Reading_For_An_Inactive_Or_Deleted_Account(bool isActive, bool isDeleted)
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Set_Account_State_Async(host, email, isActive, isDeleted);

        //Act
        var result = await Get_Preferences_Async(host, userId);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Refuse_Reading_For_An_Unconfirmed_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var email = await host.RegisterUserAsync();   // registered, never confirmed
        var user = await Find_User_Async(host, email);
        Assert.NotNull(user);

        //Act
        var result = await Get_Preferences_Async(host, user.Id);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Refuse_Reading_For_A_Locked_Out_Account()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Lock_Account_Async(host, email);

        //Act
        var result = await Get_Preferences_Async(host, userId);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Refuse_Reading_For_A_User_That_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Get_Preferences_Async(host, 999999);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Give_The_Same_Answer_For_Every_Kind_Of_Account_Refusal_When_Reading()
    {
        //Arrange
        await using var host = Make_Host();

        var (deactivatedEmail, deactivatedId) = await Create_Confirmed_User_Async(host);
        await Set_Account_State_Async(host, deactivatedEmail, isActive: false, isDeleted: false);

        var (lockedEmail, lockedId) = await Create_Confirmed_User_Async(host);
        await Lock_Account_Async(host, lockedEmail);

        //Act
        var results = new[]
        {
            await Get_Preferences_Async(host, deactivatedId),
            await Get_Preferences_Async(host, lockedId),
            await Get_Preferences_Async(host, 999999)
        };

        //Assert (the answer never says why, so it can't be used to probe accounts)
        Assert.All(results, r => Assert.False(r.Succeeded));
        Assert.All(results, r => Assert.Equal(results[0].ErrorCode, r.ErrorCode));
        Assert.All(results, r => Assert.Equal(results[0].Errors, r.Errors));
    }

    // DESIGN DECISION: a missing row is a bug on our side. It is refused and logged, never repaired.
    [Fact]
    public async Task Refuse_And_Create_Nothing_When_The_Preference_Row_Is_Missing_On_A_Read()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Delete_Preference_Row_Async(host, userId);

        //Act
        var result = await Get_Preferences_Async(host, userId);

        //Assert (not AccountUnavailable: that would send the user back to sign in)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.MissingPreferences, result.ErrorCode);
        Assert.Null(result.Data);
        Assert.Null(await Find_Preference_Async(host, userId));
    }
    #endregion

    #region shape
    // The response lists exactly what the settings screen shows
    [Fact]
    public void Contain_Exactly_The_Fields_The_Settings_Screen_Shows()
    {
        //Arrange
        var expected = new[]
        {
            "MemoryChoiceMade", "MemoryEnabled", "NotificationsEnabled", "PreferredLanguage", "TimeZone"
        };

        //Act
        var actual = typeof(UserPreferenceView).GetProperties()
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        //Assert
        Assert.Equal(expected, actual);
    }

    // The request has no timestamp and no "choice made" flag, so a client can't forge a consent record
    [Fact]
    public void Contain_Exactly_The_Settings_A_Client_May_Change()
    {
        //Arrange
        var expected = new[] { "MemoryEnabled", "NotificationsEnabled", "PreferredLanguage", "TimeZone" };

        //Act
        var actual = typeof(PreferenceUpdateView).GetProperties()
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        //Assert
        Assert.Equal(expected, actual);
    }
    #endregion

    #region update preferences - what changes
    [Fact]
    public async Task Change_Only_The_Language_And_Leave_Everything_Else()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, language: "fr");

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal("fr", result.Data!.PreferredLanguage);

        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("fr", row.PreferredLanguage);
        Assert.Equal("UTC", row.TimeZone);
        Assert.True(row.NotificationsEnabled);
        Assert.True(row.MemoryEnabled);
        Assert.Null(row.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Change_Only_The_Time_Zone_And_Leave_Everything_Else()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, timeZone: "America/Edmonton");

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal("America/Edmonton", result.Data!.TimeZone);

        // Checked in the database, because the view would show "en" even for a wiped (null) language
        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("America/Edmonton", row.TimeZone);
        Assert.Equal("en", row.PreferredLanguage);
        Assert.True(row.NotificationsEnabled);
        Assert.True(row.MemoryEnabled);
        Assert.Null(row.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Change_Only_Notifications_And_Leave_Everything_Else()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, notifications: false);

        //Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data!.NotificationsEnabled);

        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.False(row.NotificationsEnabled);
        Assert.Equal("en", row.PreferredLanguage);
        Assert.Equal("UTC", row.TimeZone);
        Assert.True(row.MemoryEnabled);
        Assert.Null(row.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Change_Several_Settings_At_Once()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(
            host, userId, language: "fr", timeZone: "America/Edmonton", notifications: false, memory: false);

        //Assert
        Assert.True(result.Succeeded);
        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("fr", row.PreferredLanguage);
        Assert.Equal("America/Edmonton", row.TimeZone);
        Assert.False(row.NotificationsEnabled);
        Assert.False(row.MemoryEnabled);
    }

    [Fact]
    public async Task Return_The_Same_Preferences_A_Later_Read_Returns()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var updated = await Update_Preferences_Async(host, userId, language: "fr", memory: false);
        var read = await Get_Preferences_Async(host, userId);

        //Assert (the client sees exactly what was saved)
        Assert.Equal(read.Data!.PreferredLanguage, updated.Data!.PreferredLanguage);
        Assert.Equal(read.Data.TimeZone, updated.Data.TimeZone);
        Assert.Equal(read.Data.NotificationsEnabled, updated.Data.NotificationsEnabled);
        Assert.Equal(read.Data.MemoryEnabled, updated.Data.MemoryEnabled);
        Assert.Equal(read.Data.MemoryChoiceMade, updated.Data.MemoryChoiceMade);
    }

    [Fact]
    public async Task Not_Change_Another_Users_Preferences()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, mine) = await Create_Confirmed_User_Async(host);
        var (_, other) = await Create_Confirmed_User_Async(host);

        //Act
        await Update_Preferences_Async(host, mine, language: "fr", notifications: false, memory: false);

        //Assert
        var row = await Find_Preference_Async(host, other);
        Assert.NotNull(row);
        Assert.Equal("en", row.PreferredLanguage);
        Assert.True(row.NotificationsEnabled);
        Assert.True(row.MemoryEnabled);
        Assert.Null(row.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Stamp_The_Preferences_With_The_Current_Utc_Time_When_Something_Changes()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        var before = DateTime.UtcNow;

        //Act
        await Update_Preferences_Async(host, userId, notifications: false);
        var after = DateTime.UtcNow;

        //Assert
        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.InRange(row.UpdatedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }
    #endregion

    #region update preferences - the memory choice
    [Fact]
    public async Task Record_The_Memory_Choice_When_Memory_Is_Turned_Off()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        var before = DateTime.UtcNow;

        //Act
        var result = await Update_Preferences_Async(host, userId, memory: false);
        var after = DateTime.UtcNow;

        //Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data!.MemoryEnabled);
        Assert.True(result.Data.MemoryChoiceMade);

        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.False(row.MemoryEnabled);
        Assert.NotNull(row.MemoryChoiceMadeAt);
        Assert.InRange(row.MemoryChoiceMadeAt.Value, before.AddSeconds(-1), after.AddSeconds(1));
    }

    // Keeping the default is still a choice, so sending the same value counts
    [Fact]
    public async Task Record_The_Memory_Choice_When_Memory_Is_Sent_With_The_Value_It_Already_Has()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, memory: true);

        //Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Data!.MemoryEnabled);
        Assert.True(result.Data.MemoryChoiceMade);

        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.NotNull(row.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Not_Record_A_Memory_Choice_When_Memory_Is_Not_Sent()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, language: "fr", notifications: false);

        //Assert (changing other settings must never count as answering the memory question)
        Assert.True(result.Succeeded);
        Assert.False(result.Data!.MemoryChoiceMade);
        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Null(row.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Keep_The_Original_Choice_Time_When_Only_Another_Setting_Changes()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Update_Preferences_Async(host, userId, memory: false);
        var chosenAt = (await Find_Preference_Async(host, userId))!.MemoryChoiceMadeAt;
        Assert.NotNull(chosenAt);

        //Act
        await Update_Preferences_Async(host, userId, notifications: false);

        //Assert
        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal(chosenAt, row.MemoryChoiceMadeAt);
    }
    #endregion

    #region update preferences - what is sent
    // DESIGN DECISION: an empty request changes nothing, and does not even touch UpdatedAt
    [Fact]
    public async Task Change_Nothing_When_The_Request_Is_Empty()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        var before = await Find_Preference_Async(host, userId);
        Assert.NotNull(before);

        //Act
        var result = await Update_Preferences_Async(host, userId);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal("en", result.Data!.PreferredLanguage);
        Assert.Equal("UTC", result.Data.TimeZone);

        var after = await Find_Preference_Async(host, userId);
        Assert.NotNull(after);
        Assert.Equal(before.PreferredLanguage, after.PreferredLanguage);
        Assert.Equal(before.TimeZone, after.TimeZone);
        Assert.Equal(before.NotificationsEnabled, after.NotificationsEnabled);
        Assert.Equal(before.MemoryEnabled, after.MemoryEnabled);
        Assert.Equal(before.MemoryChoiceMadeAt, after.MemoryChoiceMadeAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    // DESIGN DECISION: a blank string means "not sent", so it never overwrites a stored value
    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public async Task Treat_Blank_Text_As_Not_Sent(string language, string timeZone)
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Set_Language_Async(host, userId, "fr");

        //Act
        var result = await Update_Preferences_Async(host, userId, language: language, timeZone: timeZone);

        //Assert
        Assert.True(result.Succeeded);
        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("fr", row.PreferredLanguage);
        Assert.Equal("UTC", row.TimeZone);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("FR")]
    [InlineData("  Fr  ")]
    public async Task Store_The_Language_Trimmed_And_In_Lowercase(string sent)
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, language: sent);

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal("fr", (await Find_Preference_Async(host, userId))!.PreferredLanguage);
    }

    // Stored as the client sent it (trimmed), not as the id the operating system reports
    [Fact]
    public async Task Store_The_Time_Zone_Trimmed()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, timeZone: "  America/Edmonton  ");

        //Assert
        Assert.True(result.Succeeded);
        Assert.Equal("America/Edmonton", (await Find_Preference_Async(host, userId))!.TimeZone);
    }
    #endregion

    #region update preferences - bad input
    [Theory]
    [InlineData("xx")]
    [InlineData("english")]
    [InlineData("de")]
    public async Task Refuse_A_Language_That_Is_Not_Supported_And_Change_Nothing(string language)
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act (notifications is valid, but the whole request must be refused)
        var result = await Update_Preferences_Async(host, userId, language: language, notifications: false);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidLanguage, result.ErrorCode);

        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("en", row.PreferredLanguage);
        Assert.True(row.NotificationsEnabled);
    }

    [Theory]
    [InlineData("Mars/Phobos")]
    [InlineData("Not A Zone")]
    public async Task Refuse_A_Time_Zone_That_Does_Not_Exist_And_Change_Nothing(string timeZone)
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var result = await Update_Preferences_Async(host, userId, timeZone: timeZone, memory: false);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.InvalidTimeZone, result.ErrorCode);

        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("UTC", row.TimeZone);
        Assert.True(row.MemoryEnabled);
        Assert.Null(row.MemoryChoiceMadeAt);   // a refused request records no choice
    }
    #endregion

    #region update preferences - refusals
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
        await Set_Account_State_Async(host, email, isActive, isDeleted);

        //Act
        var result = await Update_Preferences_Async(host, userId, language: "fr", memory: false);

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);

        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("en", row.PreferredLanguage);
        Assert.True(row.MemoryEnabled);
        Assert.Null(row.MemoryChoiceMadeAt);
    }

    [Fact]
    public async Task Refuse_An_Update_On_A_Locked_Out_Account_And_Change_Nothing()
    {
        //Arrange
        await using var host = Make_Host();
        var (email, userId) = await Create_Confirmed_User_Async(host);
        await Lock_Account_Async(host, email);

        //Act
        var result = await Update_Preferences_Async(host, userId, language: "fr");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Equal("en", (await Find_Preference_Async(host, userId))!.PreferredLanguage);
    }

    [Fact]
    public async Task Refuse_An_Update_For_A_User_That_Does_Not_Exist()
    {
        //Arrange
        await using var host = Make_Host();

        //Act
        var result = await Update_Preferences_Async(host, 999999, language: "fr");

        //Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.AccountUnavailable, result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Refuse_An_Update_And_Create_Nothing_When_The_Preference_Row_Is_Missing()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);
        await Delete_Preference_Row_Async(host, userId);

        //Act
        var result = await Update_Preferences_Async(host, userId, language: "fr");

        //Assert (a fault on our side, not an account problem, so not AccountUnavailable)
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.MissingPreferences, result.ErrorCode);
        Assert.Null(await Find_Preference_Async(host, userId));
    }
    #endregion

    #region update preferences - races
    // DESIGN DECISION: only what was sent is written, so two requests that change
    // different settings at the same moment both survive.
    [Fact]
    public async Task Keep_Both_Changes_When_Two_Updates_Change_Different_Settings_At_Once()
    {
        //Arrange
        await using var host = Make_Host();
        var (_, userId) = await Create_Confirmed_User_Async(host);

        //Act
        var results = await Task.WhenAll(
            Task.Run(() => Update_Preferences_Async(host, userId, language: "fr")),
            Task.Run(() => Update_Preferences_Async(host, userId, notifications: false)));

        //Assert
        Assert.All(results, r => Assert.True(r.Succeeded));
        var row = await Find_Preference_Async(host, userId);
        Assert.NotNull(row);
        Assert.Equal("fr", row.PreferredLanguage);
        Assert.False(row.NotificationsEnabled);
    }
    #endregion
}