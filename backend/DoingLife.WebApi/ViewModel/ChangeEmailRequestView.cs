using DoingLife.WebApi.Shared.Constant;
using System.ComponentModel.DataAnnotations;
using System.Transactions;

namespace DoingLife.WebApi.ViewModel
{
    public class ChangeEmailRequestView
    {
        [Required(ErrorMessage = " Enter a valid email address")]
        [MaxLength(225)]
        [RegularExpression(ValidationPatterns.Email, ErrorMessage = "Enter a valid email address")]
        public required string NewEmail { get; set; }

        [Required(ErrorMessage ="Password Required")]
        [MaxLength(128)]
        public required string CurrentPassword { get; set; }


    }
}
