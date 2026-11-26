using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.DAL;
using Microsoft.EntityFrameworkCore;

namespace DoingLife.WebApi.BLL
{
    public class SessionService : ISessionService
    {
        private readonly AppDbContext _context;

        public SessionService(AppDbContext context)
        {
            _context = context;
        }

        public async Task RevokeAllSessionsAsync(int userId)
        {
            var now = DateTime.UtcNow;
            await _context.UserSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTime?)now));
        }
    }
}
