using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class DeleteAccountRequestView
    {
        [Required( ErrorMessage = "Password is required")]
        [MinLength(8)]
        [MaxLength(128)]
        
        public required string CurrentPassword { get; set; }

 
        
    }
}
