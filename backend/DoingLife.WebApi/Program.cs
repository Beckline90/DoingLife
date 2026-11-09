using Azure.Communication.Email;
using DoingLife.WebApi.BLL;
using DoingLife.WebApi.BLL.Helper;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Helper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Azure.Identity;

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

            // Configure JWT creation and validation
            ConfigureJwt(builder);
            ConfigureAuthentication(builder);

            // Configure email (console in development, Azure otherwise)
            ConfigureEmail(builder);

            // Application services
            builder.Services.AddScoped<IAuthService, AuthService>();

            // Configure Swagger
            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter your JWT access token."
                });

                options.AddSecurityRequirement(document =>
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                    });
            });


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


            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("DoingLifeCors", policy =>
                {
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });


            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode =
                    StatusCodes.Status429TooManyRequests;

                options.AddPolicy("AuthRateLimit", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey:
                            context.Connection.RemoteIpAddress?.ToString()
                            ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
                options.AddPolicy("RefreshRateLimit", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
            });


            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedProto;

                options.ForwardLimit = 1;

                // Add the actual trusted Azure proxy address or network
                // when the hosting configuration is known.
            });



            var app = builder.Build();

            // Enable Swagger based on configuration
            if (builder.Configuration.GetValue<bool>("Swagger:Enabled"))
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            // Process forwarded headers from trusted reverse proxies
            app.UseForwardedHeaders();

            // Redirect HTTP requests to HTTPS for secure communication
            app.UseHttpsRedirection();

            // Log HTTP requests and responses for monitoring and debugging
            app.UseHttpLogging();

            // Allow requests from approved frontend origins (React and Next.js)
            app.UseCors("DoingLifeCors");

            // Validate JWT tokens and identify authenticated users
            app.UseAuthentication();

            // Limit incoming requests to protect the API from abuse
            app.UseRateLimiter();

            // Check whether authenticated users have permission to access endpoints
            app.UseAuthorization();



            // Health endpoint
            app.MapGet("/", () =>
                Results.Ok("Doing Life API is running!"));

            // Controller endpoints
            app.MapControllers();

            app.Run();
        }


        // PostgreSQL dependency injection
        private static void ConfigureDatabase(WebApplicationBuilder builder)
        {
            var connectionName = builder.Environment.IsDevelopment()
                ? "DefaultConnection"
                : "AzureConnection";

            var connectionString =
                builder.Configuration.GetConnectionString(connectionName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"{connectionName} is missing from configuration.");
            }

            builder.Services.AddDbContext<AppDbContext>(
                options => options
                    .UseNpgsql(connectionString)
                    .UseSnakeCaseNamingConvention());
        }


        // ASP.NET Core Identity registration
        private static void ConfigureIdentity(WebApplicationBuilder builder)
        {

            // Configure ASP.NET Core Data Protection.
            // These keys protect email confirmation and password reset tokens.
            var dataProtection = builder.Services.AddDataProtection().SetApplicationName("DoingLife");

            // In Azure, persist the keys in Blob Storage so they survive
            // application restarts, deployments, and instance replacements.
            if (!builder.Environment.IsDevelopment())
            {
                var blobUri = builder.Configuration["DataProtection:BlobUri"];

                if (string.IsNullOrWhiteSpace(blobUri))
                {
                    throw new InvalidOperationException(
                        "DataProtection:BlobUri is missing from configuration.");
                }

                dataProtection.PersistKeysToAzureBlobStorage(
                    new Uri(blobUri),
                    new DefaultAzureCredential());
            }


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

        // JWT settings, validated when the app starts
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

        // Console email in development, Azure Communication Services otherwise.
        // To test real email locally, set AzureEmail:Enabled to true in user secrets.
        private static void ConfigureEmail(WebApplicationBuilder builder)
        {
            var useAzure =
                !builder.Environment.IsDevelopment() ||
                builder.Configuration.GetValue<bool>("AzureEmail:Enabled");

            if (!useAzure)
            {
                builder.Services.AddScoped<IEmailService, ConsoleEmailService>();
                return;
            }

            var connectionString = builder.Configuration["AzureEmail:ConnectionString"];
            var senderAddress = builder.Configuration["AzureEmail:SenderAddress"];

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "AzureEmail:ConnectionString is missing. Set it in user secrets or app settings.");
            }

            if (string.IsNullOrWhiteSpace(senderAddress))
            {
                throw new InvalidOperationException(
                    "AzureEmail:SenderAddress is missing.");
            }

            builder.Services.AddSingleton(new EmailClient(connectionString));
            builder.Services.AddScoped<IEmailService, AzureEmailService>();
        }
    }
}