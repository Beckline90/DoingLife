namespace DoingLife.WebApi.ViewModel
{
    public class UserPreferenceView
    {
        public required string PreferredLanguage { get; set; }
        public required string TimeZone { get; set; }
        public bool NotificationsEnabled { get; set; }
        public bool MemoryEnabled { get; set; }
        public bool MemoryChoiceMade { get; set; }


    }
}
