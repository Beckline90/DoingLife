using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class ProfileView
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Bio { get; set; }
        public string? Email { get; set; }
        public DateOnly?     DateOfBirth { get; set; }

    }
}
