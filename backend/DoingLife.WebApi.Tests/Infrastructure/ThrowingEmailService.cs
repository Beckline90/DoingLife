using DoingLife.WebApi.BLL.Interfaces;

namespace DoingLife.WebApi.Tests.Infrastructure;

// Simulates the email provider being down
public sealed class ThrowingEmailService : IEmailService
{
    public Task SendAsync(string toEmail, string subject, string htmlBody)
        => throw new InvalidOperationException("Simulated email provider failure.");
}