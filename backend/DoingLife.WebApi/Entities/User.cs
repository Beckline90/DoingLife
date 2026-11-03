#nullable disable

using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;

namespace DoingLife.WebApi.Entities;

public partial class User : IdentityUser<int>
{
    public string FirstName { get; set; }

    public string LastName { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime? ScheduledPurgeAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<UserLoginHistory> UserLoginHistories { get; set; } = new List<UserLoginHistory>();

    public virtual UserPreference UserPreference { get; set; }

    public virtual UserProfile UserProfile { get; set; }

    public virtual ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();

  
}