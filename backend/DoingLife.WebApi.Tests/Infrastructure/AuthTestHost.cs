using DoingLife.WebApi.BLL;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Helper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoingLife.WebApi.Tests.Infrastructure;

public sealed class AuthTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    public FakeEmailService Email { get; } = new();

    public AuthTestHost(string connectionString)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:FrontendUrl"] = "http://localhost:3000"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddDataProtection();

        // Keep these options identical to ConfigureIdentity in Program.cs
        services.AddIdentityCore<User>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 8;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<JwtSettings>(o =>
        {
            o.Key = new string('k', 64);
            o.Issuer = "tests";
            o.Audience = "tests";
            o.AccessTokenExpirationMinutes = 15;
            o.RefreshTokenExpirationDays = 7;
        });

        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton(Email);
        services.AddSingleton<IEmailService>(Email);
        services.AddScoped<IAuthService, AuthService>();

        _provider = services.BuildServiceProvider();
    }

    // One scope per call, like one scope per HTTP request in the real app
    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}