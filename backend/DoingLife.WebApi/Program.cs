using DoingLife.WebApi.BLL;
using DoingLife.WebApi.BLL.Helper;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Helper;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace DoingLife.WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Register controllers
            builder.Services.AddControllers();

            // Configure PostgreSQL
            ConfigureDatabase(builder);

            // Configure ASP.NET Core Identity
            ConfigureIdentity(builder);

            // Configure JWT authentication
            ConfigureJwt(builder);
            ConfigureAuthentication(builder);

            // Configure Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<IEmailService, ConsoleEmailService>();

            // Configure HTTP logging
            builder.Services.AddHttpLogging(options =>
            {
                options.LoggingFields =
                    HttpLoggingFields.RequestMethod |
                    HttpLoggingFields.RequestPath |
                    HttpLoggingFields.ResponseStatusCode |
                    HttpLoggingFields.Duration;

                options.CombineLogs = true;
            });

            var app = builder.Build();

            // Development tools
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();


                app.UseHttpsRedirection();

                // HTTP request logging
                app.UseHttpLogging();

                // Authentication and authorization middleware
                app.UseAuthentication();
                app.UseAuthorization();


                // Health endpoint
                app.MapGet("/", () =>
                Results.Ok("Doing Life API is running!"));

                // Controller endpoints
                app.MapControllers();

                app.Run();
            }
        }

        // PostgreSQL dependency injection
        private static void ConfigureDatabase(
            WebApplicationBuilder builder)
        {
            var connectionString =
                builder.Configuration.GetConnectionString(
                    "DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "DefaultConnection is missing from configuration.");
            }

            builder.Services.AddDbContext<AppDbContext>(
                options => options
                    .UseNpgsql(connectionString)
                    .UseSnakeCaseNamingConvention());
        }

        // ASP.NET Core Identity registration
        private static void ConfigureIdentity(
            WebApplicationBuilder builder)
        {
            builder.Services.AddDataProtection();

            builder.Services
                .AddIdentityCore<User>(options =>
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
        }

        private static void ConfigureJwt(WebApplicationBuilder builder)
        {
            builder.Services
                .AddOptions<JwtSettings>()
                .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
                .Validate(
                    s => !string.IsNullOrWhiteSpace(s.Key) && s.Key.Length >= 32,
                    "Jwt:Key is missing or shorter than 32 characters. Set it in user secrets.")
                .Validate(
                    s => !s.Key.Contains("paste-the-generated"),
                    "Jwt:Key is still the placeholder value. Generate a real key.")
                .Validate(
                    s => !string.IsNullOrWhiteSpace(s.Issuer) && !string.IsNullOrWhiteSpace(s.Audience),
                    "Jwt:Issuer and Jwt:Audience must be set.")
                .Validate(
                    s => s.AccessTokenExpirationMinutes > 0 && s.RefreshTokenExpirationDays > 0,
                    "Jwt token lifetimes must be greater than 0.")
                .ValidateOnStart();

            builder.Services.AddScoped<ITokenService, TokenService>();
        }

        // Checks the JWT on incoming requests
        private static void ConfigureAuthentication(WebApplicationBuilder builder)
        {
            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer();

            // Reads the same validated settings used to create tokens,
            // so creating and checking a token can never disagree.
            builder.Services
                .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
                {
                    var jwt = jwtOptions.Value;

                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        NameClaimType = JwtRegisteredClaimNames.Sub,
                        RoleClaimType = ClaimTypes.Role
                    };
                });
        }
    }
}