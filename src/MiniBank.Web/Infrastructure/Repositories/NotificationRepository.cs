using Dapper;
using Microsoft.Data.SqlClient;
using MiniBank.Web.Models.Notifications;
using System.Data;
using System.Data.Common;
using System.Text;

namespace MiniBank.Web.Infrastructure.Repositories;

public class NotificationRepository
{
    private readonly IDbConnection _db;

    public NotificationRepository(IDbConnection db)
    {
        _db = db;
    }

    private async Task EnsureOpenAsync()
    {
        if (_db.State == ConnectionState.Open)
            return;

        if (_db is DbConnection dbc)
            await dbc.OpenAsync();
        else
            _db.Open();
    }

    public async Task<IReadOnlyList<NotificationFeature>> GetFeaturesAsync()
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode, CreatedAt, UpdatedAt
FROM dbo.NotificationFeatures
ORDER BY Title";

        var rows = await _db.QueryAsync<NotificationFeature>(sql);
        return rows.ToList();
    }

    public async Task<NotificationFeature?> GetFeatureAsync(string featureKey)
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT TOP 1 FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode, CreatedAt, UpdatedAt
FROM dbo.NotificationFeatures
WHERE FeatureKey = @featureKey";

        return await _db.QueryFirstOrDefaultAsync<NotificationFeature>(sql, new { featureKey });
    }

    public async Task UpsertFeatureAsync(string featureKey, bool isEnabled, byte channelMask, byte approvalMode)
    {
        await EnsureOpenAsync();
        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.NotificationFeatures WHERE FeatureKey = @featureKey)
BEGIN
    UPDATE dbo.NotificationFeatures
    SET IsEnabled = @isEnabled,
        ChannelMask = @channelMask,
        ApprovalMode = @approvalMode,
        UpdatedAt = GETUTCDATE()
    WHERE FeatureKey = @featureKey;
END
ELSE
BEGIN
    INSERT INTO dbo.NotificationFeatures (FeatureKey, Title, IsEnabled, ChannelMask, ApprovalMode)
    VALUES (@featureKey, @featureKey, @isEnabled, @channelMask, @approvalMode);
END";

        await _db.ExecuteAsync(sql, new { featureKey, isEnabled, channelMask, approvalMode });
    }

    public async Task<NotificationAutoSettings> GetAutoSettingsAsync()
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT TOP 1 SettingsId, LoanReminderEnabled, DaysBeforeDue, IncludeOverdue, RunHour, LastRunDate, UpdatedAt
FROM dbo.NotificationAutoSettings
WHERE SettingsId = 1";

        return await _db.QueryFirstOrDefaultAsync<NotificationAutoSettings>(sql) ?? new NotificationAutoSettings();
    }

    public async Task SaveAutoSettingsAsync(bool loanReminderEnabled, int daysBeforeDue, bool includeOverdue, byte runHour)
    {
        await EnsureOpenAsync();

        if (daysBeforeDue < 0) daysBeforeDue = 0;
        if (daysBeforeDue > 60) daysBeforeDue = 60;
        if (runHour > 23) runHour = 9;

        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.NotificationAutoSettings WHERE SettingsId = 1)
BEGIN
    UPDATE dbo.NotificationAutoSettings
    SET LoanReminderEnabled = @loanReminderEnabled,
        DaysBeforeDue = @daysBeforeDue,
        IncludeOverdue = @includeOverdue,
        RunHour = @runHour,
        UpdatedAt = GETUTCDATE()
    WHERE SettingsId = 1;
END
ELSE
BEGIN
    INSERT INTO dbo.NotificationAutoSettings (SettingsId, LoanReminderEnabled, DaysBeforeDue, IncludeOverdue, RunHour)
    VALUES (1, @loanReminderEnabled, @daysBeforeDue, @includeOverdue, @runHour);
END";

        await _db.ExecuteAsync(sql, new { loanReminderEnabled, daysBeforeDue, includeOverdue, runHour });
    }

    public async Task<bool> TryStartAutoRunAsync(DateTime localDate)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationAutoSettings
SET LastRunDate = @localDate,
    UpdatedAt = GETUTCDATE()
WHERE SettingsId = 1
  AND (LastRunDate IS NULL OR LastRunDate < @localDate);";

        return await _db.ExecuteAsync(sql, new { localDate = localDate.Date }) > 0;
    }

    public async Task<long?> EnqueueAsync(NotificationQueueItem item)
    {
        await EnsureOpenAsync();

        const string sql = @"
INSERT INTO dbo.NotificationQueue
(
    EventKey, FeatureKey, MemberId, HouseholdId, HeadMemberId, MemberIdsJson,
    RecipientMobile, RecipientName, SmsTemplateId, SmsParamsJson, MessageText,
    BaleMessageText, ChannelMask, Status, CreatedBy, ScheduledAt, NextAttemptAt
)
OUTPUT INSERTED.NotificationId
VALUES
(
    @EventKey, @FeatureKey, @MemberId, @HouseholdId, @HeadMemberId, @MemberIdsJson,
    @RecipientMobile, @RecipientName, @SmsTemplateId, @SmsParamsJson, @MessageText,
    @BaleMessageText, @ChannelMask, @Status, @CreatedBy, @ScheduledAt, @NextAttemptAt
);";

        try
        {
            return await _db.QuerySingleAsync<long>(sql, item);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return null;
        }
    }

    public async Task<NotificationQueueItem?> GetAsync(long notificationId)
    {
        await EnsureOpenAsync();
        const string sql = "SELECT TOP 1 * FROM dbo.NotificationQueue WHERE NotificationId = @notificationId";
        return await _db.QueryFirstOrDefaultAsync<NotificationQueueItem>(sql, new { notificationId });
    }

    public async Task<bool> TryStartAsync(long notificationId)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 2,
    Attempts = Attempts + 1,
    NextAttemptAt = DATEADD(MINUTE, 10, GETUTCDATE())
WHERE NotificationId = @notificationId
  AND Status = 1;";

        return await _db.ExecuteAsync(sql, new { notificationId }) > 0;
    }

    public async Task<IReadOnlyList<NotificationQueueItem>> ClaimDueAsync(int take = 25)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE TOP (@take) dbo.NotificationQueue
SET Status = 2,
    Attempts = Attempts + 1,
    NextAttemptAt = DATEADD(MINUTE, 10, GETUTCDATE())
OUTPUT INSERTED.*
WHERE Status = 1
  AND (NextAttemptAt IS NULL OR NextAttemptAt <= GETUTCDATE());";

        var rows = await _db.QueryAsync<NotificationQueueItem>(sql, new { take });
        return rows.ToList();
    }

    public async Task ResetStaleAsync()
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 1,
    NextAttemptAt = GETUTCDATE()
WHERE Status = 2
  AND NextAttemptAt < GETUTCDATE();";

        await _db.ExecuteAsync(sql);
    }

    public async Task MarkSentAsync(
        long notificationId,
        bool smsSent,
        long? smsLogId,
        bool baleSent,
        long? baleLogId,
        string? lastError)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 3,
    SentAt = GETUTCDATE(),
    SmsSentAt = CASE WHEN @smsSent = 1 THEN GETUTCDATE() ELSE SmsSentAt END,
    SmsLogId = COALESCE(@smsLogId, SmsLogId),
    BaleSentAt = CASE WHEN @baleSent = 1 THEN GETUTCDATE() ELSE BaleSentAt END,
    BaleLogId = COALESCE(@baleLogId, BaleLogId),
    LastError = @lastError,
    NextAttemptAt = NULL
WHERE NotificationId = @notificationId;";

        await _db.ExecuteAsync(sql, new { notificationId, smsSent, smsLogId, baleSent, baleLogId, lastError });
    }

    public async Task MarkSkippedAsync(long notificationId, string? reason)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 5,
    LastError = @reason,
    NextAttemptAt = NULL
WHERE NotificationId = @notificationId;";

        await _db.ExecuteAsync(sql, new { notificationId, reason });
    }

    public async Task MarkFailedAsync(
        long notificationId,
        string? error,
        bool smsSent,
        long? smsLogId,
        bool baleSent,
        long? baleLogId)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 4,
    LastError = @error,
    NextAttemptAt = NULL,
    SmsSentAt = CASE WHEN @smsSent = 1 THEN GETUTCDATE() ELSE SmsSentAt END,
    SmsLogId = COALESCE(@smsLogId, SmsLogId),
    BaleSentAt = CASE WHEN @baleSent = 1 THEN GETUTCDATE() ELSE BaleSentAt END,
    BaleLogId = COALESCE(@baleLogId, BaleLogId)
WHERE NotificationId = @notificationId;";

        await _db.ExecuteAsync(sql, new { notificationId, error, smsSent, smsLogId, baleSent, baleLogId });
    }

    public async Task ScheduleRetryAsync(
        long notificationId,
        int delayMinutes,
        string? error,
        bool smsSent,
        long? smsLogId,
        bool baleSent,
        long? baleLogId)
    {
        await EnsureOpenAsync();

        if (delayMinutes < 1) delayMinutes = 1;

        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 1,
    NextAttemptAt = DATEADD(MINUTE, @delayMinutes, GETUTCDATE()),
    LastError = @error,
    SmsSentAt = CASE WHEN @smsSent = 1 THEN GETUTCDATE() ELSE SmsSentAt END,
    SmsLogId = COALESCE(@smsLogId, SmsLogId),
    BaleSentAt = CASE WHEN @baleSent = 1 THEN GETUTCDATE() ELSE BaleSentAt END,
    BaleLogId = COALESCE(@baleLogId, BaleLogId)
WHERE NotificationId = @notificationId;";

        await _db.ExecuteAsync(sql, new { notificationId, delayMinutes, error, smsSent, smsLogId, baleSent, baleLogId });
    }

    public async Task<int> ApproveAsync(IEnumerable<long> notificationIds, string? approvedBy)
    {
        await EnsureOpenAsync();

        var ids = (notificationIds ?? Array.Empty<long>()).Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return 0;

        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 1,
    ApprovedBy = @approvedBy,
    ApprovedAt = GETUTCDATE(),
    NextAttemptAt = GETUTCDATE()
WHERE NotificationId IN @ids
  AND Status = 0;";

        return await _db.ExecuteAsync(sql, new { ids, approvedBy });
    }

    public async Task<bool> CancelAsync(long notificationId, string? cancelledBy, string? reason)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 6,
    CancelledAt = GETUTCDATE(),
    CancelledBy = @cancelledBy,
    CancelReason = @reason,
    NextAttemptAt = NULL
WHERE NotificationId = @notificationId
  AND Status IN (0, 1, 4, 5);";

        return await _db.ExecuteAsync(sql, new { notificationId, cancelledBy, reason }) > 0;
    }

    public async Task<bool> RetryAsync(long notificationId)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 1,
    Attempts = 0,
    LastError = NULL,
    NextAttemptAt = GETUTCDATE()
WHERE NotificationId = @notificationId
  AND Status IN (4, 5);";

        return await _db.ExecuteAsync(sql, new { notificationId }) > 0;
    }

    public async Task<IReadOnlyList<NotificationQueueItem>> SearchAsync(
        byte? status,
        string? featureKey,
        string? query,
        int take = 200)
    {
        await EnsureOpenAsync();

        var sql = new StringBuilder(@"
SELECT TOP (@take)
    q.*,
    LTRIM(RTRIM(CONCAT(ISNULL(m.FirstName, N''), N' ', ISNULL(m.LastName, N'')))) AS MemberFullName,
    m.MobileNumber AS MemberMobile
FROM dbo.NotificationQueue q
LEFT JOIN dbo.Members m ON m.MemberId = q.MemberId
WHERE 1=1");

        var p = new DynamicParameters();
        p.Add("take", take);

        if (status.HasValue)
        {
            sql.Append(" AND q.Status = @status");
            p.Add("status", status.Value);
        }

        if (!string.IsNullOrWhiteSpace(featureKey))
        {
            sql.Append(" AND q.FeatureKey = @featureKey");
            p.Add("featureKey", featureKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            var digits = new string(q.Where(char.IsDigit).ToArray());

            sql.Append(@"
 AND (
      CAST(q.NotificationId AS NVARCHAR(50)) LIKE @qLike
   OR CAST(q.MemberId AS NVARCHAR(50)) LIKE @qLike
   OR ISNULL(q.RecipientMobile, N'') LIKE @qLike
   OR ISNULL(q.RecipientName, N'') LIKE @qLike
   OR ISNULL(m.FirstName, N'') LIKE @qLike
   OR ISNULL(m.LastName, N'') LIKE @qLike
   OR q.MessageText LIKE @qLike
   OR (@qDigits <> '' AND CAST(q.NotificationId AS NVARCHAR(50)) LIKE @qDigitsLike)
 )");

            p.Add("qLike", "%" + q + "%");
            p.Add("qDigits", digits);
            p.Add("qDigitsLike", "%" + digits + "%");
        }

        sql.Append(" ORDER BY q.NotificationId DESC");

        var rows = await _db.QueryAsync<NotificationQueueItem>(sql.ToString(), p);
        return rows.ToList();
    }

    public async Task<int> CountPendingApprovalAsync()
    {
        await EnsureOpenAsync();
        const string sql = "SELECT COUNT(1) FROM dbo.NotificationQueue WHERE Status = 0";
        return await _db.ExecuteScalarAsync<int>(sql);
    }

    public async Task<int> ApproveAllPendingAsync(string? featureKey, string? approvedBy)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 1,
    ApprovedBy = @approvedBy,
    ApprovedAt = GETUTCDATE(),
    NextAttemptAt = GETUTCDATE()
WHERE Status = 0
  AND (@featureKey IS NULL OR FeatureKey = @featureKey);";

        return await _db.ExecuteAsync(sql, new { featureKey, approvedBy });
    }

    public async Task<int> ApprovePendingByEventKeyPrefixAsync(string eventKeyPrefix, string? approvedBy)
    {
        await EnsureOpenAsync();
        if (string.IsNullOrWhiteSpace(eventKeyPrefix))
            return 0;

        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 1,
    ApprovedBy = @approvedBy,
    ApprovedAt = GETUTCDATE(),
    NextAttemptAt = GETUTCDATE()
WHERE Status = 0
  AND EventKey LIKE @prefix + N'%';";

        return await _db.ExecuteAsync(sql, new { prefix = eventKeyPrefix.Trim(), approvedBy });
    }

    public async Task<int> RetryByEventKeyPrefixAsync(string eventKeyPrefix)
    {
        await EnsureOpenAsync();
        if (string.IsNullOrWhiteSpace(eventKeyPrefix))
            return 0;

        const string sql = @"
UPDATE dbo.NotificationQueue
SET Status = 1,
    Attempts = 0,
    LastError = NULL,
    NextAttemptAt = GETUTCDATE()
WHERE Status IN (4, 5)
  AND EventKey LIKE @prefix + N'%';";

        return await _db.ExecuteAsync(sql, new { prefix = eventKeyPrefix.Trim() });
    }

    public async Task<NotificationRunStatusSummary> GetRunStatusAsync(string featureKey, string eventKeyPrefix)
    {
        await EnsureOpenAsync();
        if (string.IsNullOrWhiteSpace(featureKey) || string.IsNullOrWhiteSpace(eventKeyPrefix))
            return new NotificationRunStatusSummary();

        const string sql = @"
SELECT
    COUNT(1) AS Total,
    ISNULL(SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END), 0) AS PendingApproval,
    ISNULL(SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END), 0) AS Queued,
    ISNULL(SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END), 0) AS Sending,
    ISNULL(SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END), 0) AS Sent,
    ISNULL(SUM(CASE WHEN Status = 4 THEN 1 ELSE 0 END), 0) AS Failed,
    ISNULL(SUM(CASE WHEN Status = 5 THEN 1 ELSE 0 END), 0) AS Skipped,
    ISNULL(SUM(CASE WHEN Status = 6 THEN 1 ELSE 0 END), 0) AS Cancelled,
    ISNULL(SUM(CASE WHEN BaleSentAt IS NOT NULL THEN 1 ELSE 0 END), 0) AS BaleSent
FROM dbo.NotificationQueue
WHERE FeatureKey = @featureKey
  AND EventKey LIKE @prefix + N'%';";

        return await _db.QuerySingleAsync<NotificationRunStatusSummary>(sql, new { featureKey, prefix = eventKeyPrefix.Trim() })
               ?? new NotificationRunStatusSummary();
    }
}
