using System.Text.RegularExpressions;
using DoingLife.WebApi.BLL.Interfaces;

namespace DoingLife.WebApi.Tests.Infrastructure;

public record SentEmail(string To, string Subject, string Body);

public sealed class FakeEmailService : IEmailService
{
    private readonly List<SentEmail> _sent = new();
    private readonly object _lock = new();

    public IReadOnlyList<SentEmail> Sent
    {
        get { lock (_lock) { return _sent.ToList(); } }
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        lock (_lock) { _sent.Add(new SentEmail(toEmail, subject, htmlBody)); }
        return Task.CompletedTask;
    }

    // Pulls userId and token out of the most recent email to this address.
    // Works for both the verification link and the password reset link.
    public (int UserId, string Token) LastLinkFor(string email)
    {
        var message = Sent.LastOrDefault(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No email was sent to {email}.");

        var match = Regex.Match(message.Body, "userId=(\\d+)&token=([^\"&]+)");
        if (!match.Success)
            throw new InvalidOperationException("The email did not contain a userId and token link.");

        return (int.Parse(match.Groups[1].Value), match.Groups[2].Value);
    }
}