using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class ChangePasswordRequestView
    {
        [Required(ErrorMessage ="Input Current Password")]
        [MaxLength(128)]
        public required string CurrentPassword { get; set; }

        [Required(ErrorMessage = "Input New Password")]
        [MaxLength(128)]
        [MinLength(8)]
        public required string NewPassword { get; set; }

    }
}
