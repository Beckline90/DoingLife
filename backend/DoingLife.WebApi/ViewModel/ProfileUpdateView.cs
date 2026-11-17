using DoingLife.WebApi.Shared.Constant;
using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class ProfileUpdateView
    {
        [Required(ErrorMessage = "First name is required.")]
        [StringLength(50, MinimumLength = 2,
            ErrorMessage = "First name must be between 2 and 50 characters.")]
        [RegularExpression(ValidationPatterns.PersonName,
            ErrorMessage = "First name can only contain letters, spaces, hyphens and apostrophes.")]
        public required string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(50, MinimumLength = 2,
            ErrorMessage = "Last name must be between 2 and 50 characters.")]
        [RegularExpression(ValidationPatterns.PersonName,
            ErrorMessage = "Last name can only contain letters, spaces, hyphens and apostrophes.")]
        public required string LastName { get; set; }

        [StringLength(500, ErrorMessage = "Bio can be at most 500 characters.")]
        public string? Bio { get; set; }


    }
   
}