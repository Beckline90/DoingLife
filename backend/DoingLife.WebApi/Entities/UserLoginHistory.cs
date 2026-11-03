#nullable disable
using System;
using System.Collections.Generic;

namespace DoingLife.WebApi.Entities;

public partial class UserLoginHistory
{
    public int UserLoginHistoryId { get; set; }

    public int? UserId { get; set; }

    public string LoginProvider { get; set; }

    public string IpAddress { get; set; }

    public string UserAgent { get; set; }

    public bool IsSuccessful { get; set; }

    public string FailureReason { get; set; }

    public DateTime AttemptedAt { get; set; }

    public virtual User User { get; set; }
}