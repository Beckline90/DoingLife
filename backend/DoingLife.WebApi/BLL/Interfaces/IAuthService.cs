using DoingLife.WebApi.ViewModel;
using DoingLife.WebApi.Shared.Helper;
namespace DoingLife.WebApi.BLL.Interfaces
{
    public interface IAuthService
    {
        Task<SResult> RegisterAsync(RegisterRequestView request);
        Task<SResult> ConfirmEmailAsync(int userId, string token);
        Task<SResult> ResendConfirmationAsync(string email);
        Task<SResult<AuthResponseView>> LoginAsync(LoginRequestView request, ClientInfoView clientInfo);
    }
}
