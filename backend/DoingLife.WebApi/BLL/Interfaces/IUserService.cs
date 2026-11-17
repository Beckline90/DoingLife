using DoingLife.WebApi.Shared.Helper;
using DoingLife.WebApi.ViewModel;

namespace DoingLife.WebApi.BLL.Interfaces
{
    public interface IUserService
    {
        Task<SResult<ProfileView>> GetProfileAsync(int userId);
        Task<SResult<ProfileView>> UpdateProfileAsync(int userId, ProfileUpdateView request);
       
    }
}
