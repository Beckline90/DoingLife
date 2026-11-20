using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Constant;
using DoingLife.WebApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DoingLife.WebApi.Tests.Endpoints;

[Collection("Database")]
public class ProfileEndpoints_Should : IAsyncLifetime
{
    private const string ProfileUrl = "/api/users/me/profile";

    private readonly DatabaseFixture _db;
    private ApiFactory _factory = null!;

    public ProfileEndpoints_Should(DatabaseFixture db) => _db = db;

    // xUnit creates a new instance of this class for every test, so every test gets its own app
    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_db.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    // What the client receives, written out on purpose so a change to the wire format shows up here
    private sealed record ProfileDto(
        string FirstName, string LastName, string Email, string? Bio, DateOnly? DateOfBirth);

    #region support methods
    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private static async Task<ProfileDto> Read_Profile_Async(HttpResponseMessage response)
    {
        var profile = await response.Content.ReadFromJsonAsync<ProfileDto>();
        Assert.NotNull(profile);
        return profile;
    }

    private static async Task<ProfileDto> Get_Profile_Async(HttpClient client)
    {
        var response = await client.GetAsync(ProfileUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Read_Profile_Async(response);
    }

    private static async Task<string?> Read_Code_Async(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task Assert_Rejected_Async(string token)
    {
        var response = await _factory.CreateApiClient(token).GetAsync(ProfileUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Changes one character in the middle of the signature, where every character matters
    private static string Tamper(string jwt)
    {
        var parts = jwt.Split('.');
        var signature = parts[2].ToCharArray();
        signature[10] = signature[10] == 'A' ? 'B' : 'A';
        parts[2] = new string(signature);
        return string.Join('.', parts);
    }

    private Task<User> Find_User_Async(int userId)
        => _factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));

    private Task<UserProfile> Find_Profile_Async(int userId)
        => _factory.WithDbAsync(db => db.UserProfiles.AsNoTracking().SingleAsync(p => p.UserId == userId));

    private Task Set_Bio_Async(int userId, string? bio)
        => _factory.WithDbAsync(db => db.UserProfiles
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Bio, bio)));

    private Task Set_Account_State_Async(int userId, bool isActive, bool isDeleted)
        => _factory.WithDbAsync(db => db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, isActive)
                .SetProperty(u => u.IsDeleted, isDeleted)));

    private Task Lock_Account_Async(int userId)
        => _factory.WithDbAsync(db => db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.LockoutEnd,
                (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(15))));

    private Task Delete_Profile_Row_Async(int userId)
        => _factory.WithDbAsync(db => db.UserProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync());
    #endregion

    #region no valid token
    [Theory]
    [InlineData("GET", "/api/users/me")]
    [InlineData("GET", ProfileUrl)]
    [InlineData("PUT", ProfileUrl)]
    public async Task Return_401_When_No_Token_Is_Sent(string method, string url)
    {
        //Arrange
        var client = _factory.CreateApiClient();
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT")
        {
            request.Content = Json("""{"firstName":"Anna","lastName":"Smith","bio":"hello"}""");
        }

        //Act
        var response = await client.SendAsync(request);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Return_401_Before_Looking_At_The_Body_When_No_Token_Is_Sent()
    {
        //Arrange
        var client = _factory.CreateApiClient();

        //Act (the body is not even valid JSON, so a 400 here would mean the body was read first)
        var response = await client.PutAsync(ProfileUrl, Json("{ not json"));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task Return_401_For_A_Token_That_Is_Not_A_Jwt(string token)
    {
        await Assert_Rejected_Async(token);
    }

    // Control: proves the hand-made tokens below are accepted when nothing is wrong with them,
    // so each rejection test fails for its one defect and nothing else
    [Fact]
    public async Task Accept_A_Hand_Made_Token_With_The_Right_Key_Issuer_And_Audience()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var token = ApiFactory.CreateJwt(user.Id.ToString());

        //Act
        var response = await _factory.CreateApiClient(token).GetAsync(ProfileUrl);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Return_401_For_A_Token_With_An_Altered_Signature()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();

        //Act and Assert
        await Assert_Rejected_Async(Tamper(user.AccessToken));
    }

    [Fact]
    public async Task Return_401_For_An_Expired_Token()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var token = ApiFactory.CreateJwt(user.Id.ToString(), expiresUtc: DateTime.UtcNow.AddMinutes(-5));

        //Act and Assert
        await Assert_Rejected_Async(token);
    }

    [Fact]
    public async Task Return_401_For_A_Token_Signed_With_A_Different_Key()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var token = ApiFactory.CreateJwt(user.Id.ToString(), signingKey: new string('x', 64));

        //Act and Assert
        await Assert_Rejected_Async(token);
    }

    [Fact]
    public async Task Return_401_For_A_Token_With_The_Wrong_Audience()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var token = ApiFactory.CreateJwt(user.Id.ToString(), audience: "someone-else");

        //Act and Assert
        await Assert_Rejected_Async(token);
    }

    [Fact]
    public async Task Return_401_For_A_Token_With_The_Wrong_Issuer()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var token = ApiFactory.CreateJwt(user.Id.ToString(), issuer: "someone-else");

        //Act and Assert
        await Assert_Rejected_Async(token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public async Task Return_401_When_The_Token_Has_No_Usable_User_Id(string? subject)
    {
        //Arrange (a genuine signature, but the id inside is missing or not a number)
        var token = ApiFactory.CreateJwt(subject);

        //Act and Assert
        await Assert_Rejected_Async(token);
    }
    #endregion

    #region reading the profile
    [Fact]
    public async Task Return_The_Profile_Of_The_Signed_In_User()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.GetAsync(ProfileUrl);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await Read_Profile_Async(response);
        Assert.Equal(user.Email, profile.Email);
        Assert.Equal("Test", profile.FirstName);
        Assert.Equal("User", profile.LastName);
        Assert.Null(profile.Bio);
        Assert.NotNull(profile.DateOfBirth);
    }

    [Fact]
    public async Task Return_The_Bio_Saved_In_The_Database()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        await Set_Bio_Async(user.Id, "Exploring careers in nursing.");

        //Act
        var profile = await Get_Profile_Async(_factory.CreateApiClient(user.AccessToken));

        //Assert
        Assert.Equal("Exploring careers in nursing.", profile.Bio);
    }

    // Nothing else may leave the server: no password hash, no flags, no ids
    [Fact]
    public async Task Send_Only_The_Documented_Fields_Over_The_Wire()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.GetAsync(ProfileUrl);

        //Assert
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "bio", "dateOfBirth", "email", "firstName", "lastName" }, names);
    }

    [Fact]
    public async Task Return_The_Identity_Summary_For_Me()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.GetAsync("/api/users/me");

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(user.Email, doc.RootElement.GetProperty("email").GetString());
        Assert.True(doc.RootElement.GetProperty("emailConfirmed").GetBoolean());
    }
    #endregion

    #region ownership
    [Fact]
    public async Task Return_Each_User_Only_Their_Own_Profile()
    {
        //Arrange
        var a = await _factory.CreateUserAsync();
        var b = await _factory.CreateUserAsync();
        await Set_Bio_Async(a.Id, "Bio of A");
        await Set_Bio_Async(b.Id, "Bio of B");

        //Act
        var profileA = await Get_Profile_Async(_factory.CreateApiClient(a.AccessToken));
        var profileB = await Get_Profile_Async(_factory.CreateApiClient(b.AccessToken));

        //Assert
        Assert.Equal(a.Email, profileA.Email);
        Assert.Equal("Bio of A", profileA.Bio);
        Assert.Equal(b.Email, profileB.Email);
        Assert.Equal("Bio of B", profileB.Bio);
    }

    [Fact]
    public async Task Ignore_A_User_Id_In_The_Query_String()
    {
        //Arrange
        var a = await _factory.CreateUserAsync();
        var b = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(a.AccessToken);

        //Act (A asks for B's profile by putting B's id in the address)
        var response = await client.GetAsync($"{ProfileUrl}?userId={b.Id}&id={b.Id}");

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(a.Email, (await Read_Profile_Async(response)).Email);
    }

    [Fact]
    public async Task Not_Offer_A_Route_That_Takes_A_User_Id()
    {
        //Arrange
        var a = await _factory.CreateUserAsync();
        var b = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(a.AccessToken);

        //Act
        var response = await client.GetAsync($"/api/users/{b.Id}/profile");

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ignore_A_User_Id_Sent_In_The_Body_And_Change_Only_The_Callers_Profile()
    {
        //Arrange
        var a = await _factory.CreateUserAsync();
        var b = await _factory.CreateUserAsync();
        await Set_Bio_Async(b.Id, "B's bio");
        var client = _factory.CreateApiClient(a.AccessToken);

        //Act (A's request names B)
        var response = await client.PutAsJsonAsync(ProfileUrl, new
        {
            firstName = "Anna",
            lastName = "Smith",
            bio = "A's new bio",
            userId = b.Id
        });

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var userA = await Find_User_Async(a.Id);
        Assert.Equal("Anna", userA.FirstName);
        Assert.Equal("A's new bio", (await Find_Profile_Async(a.Id)).Bio);

        var userB = await Find_User_Async(b.Id);
        Assert.Equal("Test", userB.FirstName);
        Assert.Equal("User", userB.LastName);
        Assert.Equal("B's bio", (await Find_Profile_Async(b.Id)).Bio);
    }

    [Fact]
    public async Task Ignore_Fields_A_Client_May_Not_Change()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var before = await Find_Profile_Async(user.Id);
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act (extra fields that a client has no business sending)
        var response = await client.PutAsJsonAsync(ProfileUrl, new
        {
            firstName = "Anna",
            lastName = "Smith",
            bio = "hello",
            email = "attacker@example.com",
            dateOfBirth = "1990-01-01",
            isActive = false,
            isDeleted = true,
            emailConfirmed = false,
            passwordHash = "x"
        });

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await Find_User_Async(user.Id);
        Assert.Equal(user.Email, after.Email);
        Assert.True(after.IsActive);
        Assert.False(after.IsDeleted);
        Assert.True(after.EmailConfirmed);
        Assert.Equal(before.DateOfBirth, (await Find_Profile_Async(user.Id)).DateOfBirth);
    }
    #endregion

    #region updating the profile
    [Fact]
    public async Task Save_The_Profile_And_Return_The_Saved_Values()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.PutAsJsonAsync(ProfileUrl, new
        {
            firstName = "Mary-Jane",
            lastName = "O'Brien",
            bio = "I want to study in Alberta."
        });

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Read_Profile_Async(response);
        Assert.Equal("Mary-Jane", saved.FirstName);
        Assert.Equal("O'Brien", saved.LastName);
        Assert.Equal("I want to study in Alberta.", saved.Bio);
        Assert.Equal(user.Email, saved.Email);

        // A later read returns exactly what the update returned, and the database agrees
        Assert.Equal(saved, await Get_Profile_Async(client));
        var row = await Find_User_Async(user.Id);
        Assert.Equal("Mary-Jane", row.FirstName);
        Assert.Equal("O'Brien", row.LastName);
        Assert.Equal("I want to study in Alberta.", (await Find_Profile_Async(user.Id)).Bio);
    }

    // DESIGN DECISION: PUT replaces the whole profile, so a body with no bio clears the bio.
    [Fact]
    public async Task Clear_The_Bio_When_The_Body_Has_No_Bio_Field()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        await Set_Bio_Async(user.Id, "bio that was not sent back");
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.PutAsJsonAsync(ProfileUrl, new { firstName = "Anna", lastName = "Smith" });

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await Read_Profile_Async(response)).Bio);
        Assert.Null((await Find_Profile_Async(user.Id)).Bio);
    }

    // DESIGN DECISION: the update replaces the whole profile, so there is no PATCH.
    [Fact]
    public async Task Not_Offer_Patch()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);
        var request = new HttpRequestMessage(HttpMethod.Patch, ProfileUrl)
        {
            Content = Json("""{"bio":"only the bio"}""")
        };

        //Act
        var response = await client.SendAsync(request);

        //Assert
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
    #endregion

    #region bad input (the attributes on the request model run here for the first time)
    [Theory]
    [InlineData("A", "Smith")]         // too short
    [InlineData("Anna123", "Smith")]   // digits
    [InlineData("Anna", "S")]
    [InlineData("Anna", "Sm1th")]
    public async Task Return_400_And_Change_Nothing_For_Bad_Names(string firstName, string lastName)
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);
        var before = await Get_Profile_Async(client);

        //Act
        var response = await client.PutAsJsonAsync(ProfileUrl, new { firstName, lastName, bio = "attempted" });

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await Get_Profile_Async(client));
    }

    [Fact]
    public async Task Return_400_For_A_Name_Over_The_Length_Limit()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.PutAsJsonAsync(ProfileUrl,
            new { firstName = new string('a', 51), lastName = "Smith" });

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Accept_A_Bio_Of_Exactly_The_Limit_And_Reject_One_Character_More()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var atLimit = await client.PutAsJsonAsync(ProfileUrl,
            new { firstName = "Anna", lastName = "Smith", bio = new string('b', 500) });
        var overLimit = await client.PutAsJsonAsync(ProfileUrl,
            new { firstName = "Anna", lastName = "Smith", bio = new string('b', 501) });

        //Assert
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overLimit.StatusCode);
        Assert.Equal(500, (await Get_Profile_Async(client)).Bio!.Length);
    }

    [Theory]
    [InlineData("""{"lastName":"Smith"}""")]
    [InlineData("""{"firstName":"Anna"}""")]
    [InlineData("{}")]
    public async Task Return_400_When_A_Name_Is_Missing_From_The_Body(string body)
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);
        var before = await Get_Profile_Async(client);

        //Act
        var response = await client.PutAsync(ProfileUrl, Json(body));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await Get_Profile_Async(client));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public async Task Return_400_For_A_Body_That_Is_Not_Usable_Json(string body)
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.PutAsync(ProfileUrl, Json(body));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // What your partner's form receives when a name is too short. If you later reshape
    // validation errors, update this test.
    [Fact]
    public async Task Tell_The_Client_Why_A_Name_Was_Rejected()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);

        //Act
        var response = await client.PutAsJsonAsync(ProfileUrl, new { firstName = "A", lastName = "Smith" });

        //Assert
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("First name must be between 2 and 50 characters.", body);
    }
    #endregion

    #region a valid token for an account that is no longer allowed
    [Theory]
    [InlineData(false, false)]   // deactivated
    [InlineData(true, true)]     // deleted
    public async Task Return_401_With_A_Code_When_The_Account_Is_Closed_But_The_Token_Is_Still_Valid(
        bool isActive, bool isDeleted)
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ProfileUrl)).StatusCode);   // the token works
        await Set_Account_State_Async(user.Id, isActive, isDeleted);

        //Act
        var response = await client.GetAsync(ProfileUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.AccountUnavailable, await Read_Code_Async(response));
    }

    [Fact]
    public async Task Return_401_And_Change_Nothing_When_A_Closed_Account_Tries_To_Update()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        await Set_Bio_Async(user.Id, "original bio");
        var client = _factory.CreateApiClient(user.AccessToken);
        await Set_Account_State_Async(user.Id, isActive: false, isDeleted: false);

        //Act
        var response = await client.PutAsJsonAsync(ProfileUrl,
            new { firstName = "Changed", lastName = "Name", bio = "attempted bio" });

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.AccountUnavailable, await Read_Code_Async(response));

        var row = await Find_User_Async(user.Id);
        Assert.Equal("Test", row.FirstName);
        Assert.Equal("User", row.LastName);
        Assert.Equal("original bio", (await Find_Profile_Async(user.Id)).Bio);
    }

    [Fact]
    public async Task Return_401_For_A_Locked_Out_Account()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);
        await Lock_Account_Async(user.Id);

        //Act
        var response = await client.GetAsync(ProfileUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.AccountUnavailable, await Read_Code_Async(response));
    }

    [Fact]
    public async Task Return_401_For_A_Valid_Token_Whose_User_Does_Not_Exist()
    {
        //Arrange
        var token = ApiFactory.CreateJwt("999999");

        //Act
        var response = await _factory.CreateApiClient(token).GetAsync(ProfileUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.AccountUnavailable, await Read_Code_Async(response));
    }
    #endregion

    #region a fault on our side
    [Fact]
    public async Task Return_500_With_A_Code_When_The_Profile_Row_Is_Missing()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);
        await Delete_Profile_Row_Async(user.Id);

        //Act
        var response = await client.GetAsync(ProfileUrl);

        //Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ErrorCodes.FailedProfile, await Read_Code_Async(response));

        // The detail goes to the log, never to the client
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("has no profile row", body);
        Assert.DoesNotContain("Exception", body);
    }

    [Fact]
    public async Task Return_500_And_Change_Nothing_When_The_Profile_Row_Is_Missing_On_Update()
    {
        //Arrange
        var user = await _factory.CreateUserAsync();
        var client = _factory.CreateApiClient(user.AccessToken);
        await Delete_Profile_Row_Async(user.Id);

        //Act
        var response = await client.PutAsJsonAsync(ProfileUrl,
            new { firstName = "Changed", lastName = "Name", bio = "attempted bio" });

        //Assert (the names on the user row must not change either)
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ErrorCodes.FailedProfile, await Read_Code_Async(response));

        var row = await Find_User_Async(user.Id);
        Assert.Equal("Test", row.FirstName);
        Assert.Equal("User", row.LastName);
    }
    #endregion
}