
using DoingLife.WebApi.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpLogging;

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
                options => options.UseNpgsql(connectionString));
        }
    }
}
