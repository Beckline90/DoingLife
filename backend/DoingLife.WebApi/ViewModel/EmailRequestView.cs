using System.ComponentModel.DataAnnotations;
namespace DoingLife.WebApi.ViewModel
{
    public class EmailRequestView
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

    }
}
