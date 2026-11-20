#nullable disable
using System;
using System.Collections.Generic;

namespace DoingLife.WebApi.Entities;

public partial class UserPreference
{
    public int UserPreferenceId { get; set; }

    public int UserId { get; set; }

    public string PreferredLanguage { get; set; }

    public string TimeZone { get; set; }

    public bool NotificationsEnabled { get; set; }

    public bool MemoryEnabled { get; set; }

    public bool VoiceEnabled { get; set; }

    public string PreferredVoice { get; set; }

    public decimal SpeechRate { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; }
}