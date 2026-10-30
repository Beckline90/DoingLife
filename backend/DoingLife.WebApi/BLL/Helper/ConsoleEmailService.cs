using DoingLife.WebApi.BLL.Interfaces;

namespace DoingLife.WebApi.BLL.Helper;

// Development only: writes the email to the log instead of sending it.
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        _logger.LogInformation("EMAIL to {To} | {Subject}{NewLine}{Body}",
            toEmail, subject, Environment.NewLine, htmlBody);
        return Task.CompletedTask;
    }
}