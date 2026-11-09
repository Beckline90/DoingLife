using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class ConfirmEmailRequestView
    {
        [Range(1, int.MaxValue, ErrorMessage = "User id is required.")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Token is required.")]
        public string Token { get; set; } = string.Empty;
    }
}