using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Tests.Infrastructure;
using DoingLife.WebApi.ViewModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DoingLife.WebApi.Tests.Auth;

[Collection("Database")]
public class RegisterTests
{
    private readonly DatabaseFixture _db;

    public RegisterTests(DatabaseFixture db) => _db = db;

    [Fact]
    public async Task Register_WithValidRequest_CreatesUserProfileAndPreferences()
    {
        await using var host = new AuthTestHost(_db.ConnectionString);
        var email = $"{Guid.NewGuid():N}@example.com";

        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await auth.RegisterAsync(new RegisterRequestView
        {
            Email = email,
            Password = "Test1234!",
            FirstName = "Test",
            LastName = "User",
            DateOfBirth = new DateOnly(1995, 5, 14),
            AcceptedTerms = true
        });

        Assert.True(result.Succeeded);

        var user = await context.Users
            .Include(u => u.UserProfile)
            .Include(u => u.UserPreference)
            .SingleAsync(u => u.Email == email);

        Assert.NotNull(user.UserProfile);
        Assert.NotNull(user.UserPreference);
        Assert.False(user.EmailConfirmed);
        Assert.Single(host.Email.Sent, m => m.To == email);
    }
}