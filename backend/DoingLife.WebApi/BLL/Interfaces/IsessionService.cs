namespace DoingLife.WebApi.BLL.Interfaces
{
    public interface ISessionService
    {
        Task RevokeAllSessionsAsync(int userId);
    }
}
