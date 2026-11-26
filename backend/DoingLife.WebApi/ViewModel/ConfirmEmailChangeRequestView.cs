using DoingLife.WebApi.Shared.Constant;
using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class ConfirmEmailChangeRequestView
    {
       
            
            [Range(1, int.MaxValue, ErrorMessage = "User id is required.")]
            public required int UserId { get; set; }

            [Required(ErrorMessage = "Email is required.")]
            [MaxLength(255)]
            [RegularExpression(ValidationPatterns.Email, ErrorMessage = "Enter a valid email address.")]
            public required string NewEmail { get; set; }

            [Required(ErrorMessage = "Token is required.")]
            [MaxLength(2048)]
            public required string Token { get; set; }
        

    }
}
