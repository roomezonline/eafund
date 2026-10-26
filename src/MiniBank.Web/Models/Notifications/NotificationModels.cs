namespace MiniBank.Web.Models.Notifications;

public static class NotificationChannels
{
    public const byte Sms = 1;
    public const byte Bale = 2;
    public const byte Both = Sms | Bale;

    public static bool HasSms(byte mask) => (mask & Sms) != 0;
    public static bool HasBale(byte mask) => (mask & Bale) != 0;

    public static string Label(byte mask) => mask switch
    {
        Sms => "پیامک",
        Bale => "بله",
        Both => "پیامک + بله",
        _ => "بدون کانال"
    };
}

public static class NotificationStatuses
{
    public const byte PendingApproval = 0;
    public const byte Queued = 1;
    public const byte Sending = 2;
    public const byte Sent = 3;
    public const byte Failed = 4;
    public const byte Skipped = 5;
    public const byte Cancelled = 6;

    public static string Label(byte status) => status switch
    {
        PendingApproval => "در انتظار تایید",
        Queued => "در صف",
        Sending => "در حال ارسال",
        Sent => "ارسال شد",
        Failed => "ناموفق",
        Skipped => "ارسال نشد",
        Cancelled => "لغو شده",
        _ => "نامشخص"
    };
}

public sealed class NotificationFeature
{
    public string FeatureKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public byte ChannelMask { get; set; } = NotificationChannels.Sms;
    public byte ApprovalMode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class NotificationRunStatusSummary
{
    public int Total { get; set; }
    public int PendingApproval { get; set; }
    public int Queued { get; set; }
    public int Sending { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Cancelled { get; set; }
    public int BaleSent { get; set; }
}

public sealed class NotificationQueueItem
{
    public long NotificationId { get; set; }
    public string? EventKey { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public long? MemberId { get; set; }
    public long? HouseholdId { get; set; }
    public long? HeadMemberId { get; set; }
    public string? MemberIdsJson { get; set; }
    public string? RecipientMobile { get; set; }
    public string? RecipientName { get; set; }
    public int? SmsTemplateId { get; set; }
    public string? SmsParamsJson { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string? BaleMessageText { get; set; }
    public byte ChannelMask { get; set; }
    public byte Status { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public long? SmsLogId { get; set; }
    public DateTime? SmsSentAt { get; set; }
    public long? BaleLogId { get; set; }
    public DateTime? BaleSentAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancelReason { get; set; }

    // Read-only fields populated by list queries.
    public string? MemberFullName { get; set; }
    public string? MemberMobile { get; set; }
}

public sealed class NotificationAutoSettings
{
    public int SettingsId { get; set; } = 1;
    public bool LoanReminderEnabled { get; set; }
    public int DaysBeforeDue { get; set; } = 2;
    public bool IncludeOverdue { get; set; } = true;
    public byte RunHour { get; set; } = 9;
    public DateTime? LastRunDate { get; set; }
    public DateTime UpdatedAt { get; set; }
}
