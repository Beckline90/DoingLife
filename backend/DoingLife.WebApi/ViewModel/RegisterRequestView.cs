using System.ComponentModel.DataAnnotations;
using DoingLife.WebApi.Shared.Constant;

namespace DoingLife.WebApi.ViewModel;

public class RegisterRequestView
{
    [Required(ErrorMessage = "Email is required.")]
    [MaxLength(255)]
    [RegularExpression(ValidationPatterns.Email,
        ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(50, MinimumLength = 2,
        ErrorMessage = "First name must be between 2 and 50 characters.")]
    [RegularExpression(ValidationPatterns.PersonName,
        ErrorMessage = "First name can only contain letters, spaces, hyphens and apostrophes.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(50, MinimumLength = 2,
        ErrorMessage = "Last name must be between 2 and 50 characters.")]
    [RegularExpression(ValidationPatterns.PersonName,
        ErrorMessage = "Last name can only contain letters, spaces, hyphens and apostrophes.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Date of birth is required.")]
    public DateOnly? DateOfBirth { get; set; }

    public bool AcceptedTerms { get; set; }
}