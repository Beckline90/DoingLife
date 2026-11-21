using System.ComponentModel.DataAnnotations;

namespace DoingLife.WebApi.ViewModel
{
    public class PreferenceUpdateView
    {
      
        [StringLength(20, ErrorMessage = "Preferred language can be at most 20 characters.")]
        public string? PreferredLanguage { get; set; }

        [StringLength(100, ErrorMessage = "Time zone can be at most 100 characters.")]
        public string? TimeZone { get; set; }

        public bool? NotificationsEnabled { get; set; }

        public bool? MemoryEnabled { get; set; }
    }
}