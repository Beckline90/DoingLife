using System.ComponentModel.DataAnnotations;       
namespace DoingLife.WebApi.ViewModel
{
    public class RegisterRequestView
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string FirstName { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string LastName { get; set; } = string.Empty;
    }
}
