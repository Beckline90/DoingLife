using DoingLife.WebApi.Entities;

namespace DoingLife.WebApi.BLL.Interfaces
{
    public interface ITokenService
    {
        (string Token, DateTime ExpiresAt) CreateAccessToken(User user, IEnumerable<string>roles);
        string GenerateRefreshToken();
        string HashRefreshToken(string token);
    }
}
