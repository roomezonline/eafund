namespace MiniBank.Web.Models.Notifications;

public sealed class BaleBotSettings
{
    public int SettingsId { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public string? BotToken { get; set; }
    public string? BotUsername { get; set; }
    public string? WebhookSecret { get; set; }
    public string ApiBaseUrl { get; set; } = "https://tapi.bale.ai";
    public bool RequireBaleConnectionForMembers { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class BaleSubscriber
{
    public long SubscriberId { get; set; }
    public long MemberId { get; set; }
    public long ChatId { get; set; }
    public long? BaleUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsActive { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? OptedOutAt { get; set; }

    // Read-only fields populated by list queries.
    public string? MemberFirstName { get; set; }
    public string? MemberLastName { get; set; }
    public string? MemberMobileNumber { get; set; }

    public string MemberFullName =>
        string.Join(' ', new[] { MemberFirstName, MemberLastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class BaleLinkToken
{
    public long TokenId { get; set; }
    public string Token { get; set; } = string.Empty;
    public long MemberId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public long? UsedChatId { get; set; }
}

public sealed class BaleMessageLog
{
    public long LogId { get; set; }
    public long? NotificationId { get; set; }
    public long? MemberId { get; set; }
    public long? ChatId { get; set; }
    public string? FeatureKey { get; set; }
    public string? MessageText { get; set; }
    public long? ProviderMessageId { get; set; }
    public int? ApiStatus { get; set; }
    public string? ApiMessage { get; set; }
    public bool? IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponse { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class BaleMessageLogReportRow
{
    public long LogId { get; set; }
    public long? NotificationId { get; set; }
    public long? MemberId { get; set; }
    public long? ChatId { get; set; }
    public string? FeatureKey { get; set; }
    public string? MessageText { get; set; }
    public long? ProviderMessageId { get; set; }
    public int? ApiStatus { get; set; }
    public string? ApiMessage { get; set; }
    public bool? IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponse { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    public string? MemberFirstName { get; set; }
    public string? MemberLastName { get; set; }
    public string? MemberMobileNumber { get; set; }
    public string? BaleUsername { get; set; }
    public DateTime? SubscriberLastSeenAt { get; set; }

    public string MemberFullName =>
        string.Join(' ', new[] { MemberFirstName, MemberLastName }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public bool ActivityAfterSend =>
        SentAt.HasValue && SubscriberLastSeenAt.HasValue && SubscriberLastSeenAt.Value > SentAt.Value;
}

public sealed class BaleMessageLogReportStats
{
    public int Total { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
    public int Pending { get; set; }
    public int UniqueMembers { get; set; }
    public int ActivityAfterSend { get; set; }
}

public sealed record BaleSendResult(
    bool Ok,
    long? MessageId,
    int? ApiStatus,
    string? ApiMessage,
    string? RawResponse,
    string? Error);

public sealed record BaleBotInfo(
    bool Ok,
    string? Username,
    string? FirstName,
    string? Error);
