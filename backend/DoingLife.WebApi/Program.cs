using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

            // Configure Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

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
            }

            app.UseHttpsRedirection();

            // HTTP request logging
            app.UseHttpLogging();

            // Authorization middleware
            app.UseAuthorization();

            // Health endpoint
            app.MapGet("/", () =>
                Results.Ok("Doing Life API is running!"));

            // Controller endpoints
            app.MapControllers();

            app.Run();
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
            builder.Services
                .AddIdentityCore<User>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequiredLength = 8;
                })
                .AddRoles<IdentityRole<int>>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();
        }
    }
}