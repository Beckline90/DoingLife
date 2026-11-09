using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class RefreshTokenRequestView
    {
        [Required(ErrorMessage ="Refresh token is required.")]
        public string Refreshtoken { get; set; } = string.Empty;
    }
}
