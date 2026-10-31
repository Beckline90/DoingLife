using System.ComponentModel.DataAnnotations;
namespace DoingLife.WebApi.ViewModel
{
    public class LoginRequestView
    {
        [Required(ErrorMessage = "Email is required.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; } = string.Empty;
    }
}
