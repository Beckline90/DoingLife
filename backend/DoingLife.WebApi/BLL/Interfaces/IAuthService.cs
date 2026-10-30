using DoingLife.WebApi.ViewModel;
using DoingLife.WebApi.Shared.Helper;
namespace DoingLife.WebApi.BLL.Interfaces
{
    public interface IAuthService
    {
        Task<ServiceResult> RegisterAsync(RegisterRequestView request);
        Task<ServiceResult> ConfirmEmailAsync(int userId, string token);
        Task<ServiceResult> ResendConfirmationAsync(string email);
    }
}
