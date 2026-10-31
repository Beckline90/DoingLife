namespace DoingLife.WebApi.ViewModel
{
    public class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpireAt { get; set; }
        public string RefreshToken { get; set; } = string.Empty;
        public UserDto User { get; set; } = new UserDto();

    }
}
