using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;
using Microsoft.AspNetCore.Identity;

namespace DoingLife.WebApi.BLL.Interfaces
{
    public interface IAccountService
    {
        Task<SResult> ChangeEmailAsync(int userId, ChangeEmailRequestView request);
        Task<SResult> ConfirmEmailChangeAsync(ConfirmEmailChangeRequestView request);
        Task<SResult> ChangePasswordAsync(int userId, ChangePasswordRequestView request);
        Task<SResult> DeleteAccountAsync(int userId, DeleteAccountRequestView request);
    }
}
