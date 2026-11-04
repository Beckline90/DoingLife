using Azure;
using Azure.Communication.Email;
using DoingLife.WebApi.BLL.Interfaces;

namespace DoingLife.WebApi.BLL.Helper
{
    public class AzureEmailService : IEmailService
    {
        private readonly EmailClient _emailClient;
        private readonly string _senderAddress;
        private readonly ILogger<AzureEmailService> _logger;

        public AzureEmailService(
            EmailClient emailClient,
            IConfiguration configuration,
            ILogger<AzureEmailService> logger)
        {
            _emailClient = emailClient;
            _logger = logger;

            _senderAddress = configuration["AzureEmail:SenderAddress"]
                ?? throw new InvalidOperationException("AzureEmail:SenderAddress is missing.");
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            try
            {
                // Started returns as soon as Azure accepts the message, so the
                // request doesn't wait for delivery and response times stay even.
                var operation = await _emailClient.SendAsync(
                    WaitUntil.Started,
                    _senderAddress,
                    toEmail,
                    subject,
                    htmlBody);

                _logger.LogInformation("Email send operation {OperationId} started", operation.Id);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure email service failed to send email.");
                throw; // AuthService catches this, so registration still succeeds
            }
        }
    }
}