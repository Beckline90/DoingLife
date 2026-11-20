namespace DoingLife.WebApi.ViewModel
{
    public class UpdateUserPreferenceView
    {
        public string PreferredLanguage { get; set; } = string.Empty;
        public string TimeZone { get; set; } = string.Empty;
        public bool NotificationEnabled { get; set; }
        public bool MemoryEnabled { get; set; }

    }
}
