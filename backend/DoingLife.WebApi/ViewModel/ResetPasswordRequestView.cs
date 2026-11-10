using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class ResetPasswordRequestView
    {
        [Range(1, int.MaxValue, ErrorMessage = "User id is required.")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Token is required.")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        [MinLength(8)]
        [MaxLength(128)]
        public string NewPassword { get; set; } = string.Empty;
    }
}