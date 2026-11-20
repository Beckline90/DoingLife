using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace DoingLife.WebApi.Tests.Infrastructure;

public sealed record TestUser(int Id, string Email, string AccessToken);

// Runs the real application in memory, against the Testcontainers database.
public sealed class ApiFactory : WebApplicationFactory<DoingLife.WebApi.Program>
{
    public const string JwtKey = "integration-tests-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz";
    public const string JwtIssuer = "doinglife-http-tests";
    public const string JwtAudience = "doinglife-http-tests";

    public FakeEmailService Email { get; } = new();

    public ApiFactory(string connectionString)
    {
        // Program.cs reads some settings before the app is built, so they are set as environment
        // variables, which are visible from its first line. They also win over appsettings files
        // and user secrets, so the tests behave the same on every machine and in CI.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenExpirationMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenExpirationDays", "7");
        Environment.SetEnvironmentVariable("AzureEmail__Enabled", "false");
        Environment.SetEnvironmentVariable("App__FrontendUrl", "http://localhost:3000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Emails are captured instead of printed, so tests can follow the links in them
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Email);
        });
    }

    // A client for the in-memory app. Pass a token to send it as "Authorization: Bearer ...".
    public HttpClient CreateApiClient(string? accessToken = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),   // already https, so nothing redirects
            AllowAutoRedirect = false
        });

        if (accessToken != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    // Registers, confirms and logs in a user through the service layer, not over HTTP.
    // The auth endpoints are rate limited (5 per minute per address), so setting up many
    // users over HTTP would fail with 429.
    public async Task<TestUser> CreateUserAsync()
    {
        var request = AuthTestExtensions.NewRegisterRequest();

        var registered = await WithAuthAsync(a => a.RegisterAsync(request));
        if (!registered.Succeeded)
            throw new InvalidOperationException("Test setup failed: registration did not succeed.");

        var (userId, token) = Email.LastLinkFor(request.Email);

        var confirmed = await WithAuthAsync(a => a.ConfirmEmailAsync(userId, token));
        if (!confirmed.Succeeded)
            throw new InvalidOperationException("Test setup failed: email confirmation did not succeed.");

        var login = await WithAuthAsync(a => a.LoginAsync(
            new LoginRequestView { Email = request.Email, Password = AuthTestExtensions.ValidPassword },
            new ClientInfoView { ClientIp = "127.0.0.1", UserAgent = "http-tests" }));
        if (!login.Succeeded)
            throw new InvalidOperationException("Test setup failed: login did not succeed.");

        return new TestUser(userId, request.Email, login.Data!.AccessToken);
    }

    // Reads or changes the database directly, from a fresh scope
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    // Builds a token by hand, signed like the app's own, so a test can break exactly one thing
    public static string CreateJwt(
        string? subject,
        DateTime? expiresUtc = null,
        string? signingKey = null,
        string? issuer = null,
        string? audience = null)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) };
        if (subject != null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? JwtKey)),
            SecurityAlgorithms.HmacSha256);

        var expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(15);

        var token = new JwtSecurityToken(
            issuer: issuer ?? JwtIssuer,
            audience: audience ?? JwtAudience,
            claims: claims,
            notBefore: expires.AddMinutes(-15),
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<T> WithAuthAsync<T>(Func<IAuthService, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAuthService>());
    }
}