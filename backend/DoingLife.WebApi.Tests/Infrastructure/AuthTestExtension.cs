using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace DoingLife.WebApi.Tests.Infrastructure;

public static class AuthTestExtensions
{
    public const string ValidPassword = "Test1234!";

    public static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    public static RegisterRequestView NewRegisterRequest(
        string? email = null,
        string password = ValidPassword,
        DateOnly? dateOfBirth = null,
        bool acceptedTerms = true) => new()
        {
            Email = email ?? NewEmail(),
            Password = password,
            FirstName = "Test",
            LastName = "User",
            DateOfBirth = dateOfBirth ?? new DateOnly(1995, 5, 14),
            AcceptedTerms = acceptedTerms
        };

    public static ClientInfoView NewClientInfo() => new()
    {
        ClientIp = "127.0.0.1",
        UserAgent = "tests"
    };

    // Registers a new account and returns its email
    public static async Task<string> RegisterUserAsync(this AuthTestHost host)
    {
        var request = NewRegisterRequest();

        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var result = await auth.RegisterAsync(request);

        Assert.True(result.Succeeded, "Test setup failed: registration did not succeed.");
        return request.Email;
    }

    // Registers an account and confirms its email, as a user would
    public static async Task<string> RegisterConfirmedUserAsync(this AuthTestHost host)
    {
        var email = await host.RegisterUserAsync();
        var (userId, token) = host.Email.LastLinkFor(email);

        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var result = await auth.ConfirmEmailAsync(userId, token);

        Assert.True(result.Succeeded, "Test setup failed: email confirmation did not succeed.");
        return email;
    }

    public static async Task<SResult<AuthResponseView>> LoginAsync(
        this AuthTestHost host, string email, string password = ValidPassword)
    {
        await using var scope = host.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await auth.LoginAsync(
            new LoginRequestView { Email = email, Password = password },
            NewClientInfo());
    }
}