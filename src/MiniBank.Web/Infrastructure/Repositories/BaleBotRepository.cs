using Dapper;
using Microsoft.Data.SqlClient;
using MiniBank.Web.Models.Notifications;
using MiniBank.Web.Services.Auth;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

namespace MiniBank.Web.Infrastructure.Repositories;

public class BaleBotRepository
{
    private readonly IDbConnection _db;
    private readonly CryptoService _crypto;

    public BaleBotRepository(IDbConnection db, CryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
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

    public async Task<BaleBotSettings> GetSettingsAsync()
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT TOP 1 SettingsId, IsEnabled, BotToken, BotUsername, WebhookSecret, ApiBaseUrl, RequireBaleConnectionForMembers, CreatedAt, UpdatedAt
FROM dbo.BaleBotSettings
WHERE SettingsId = 1";

        var row = await _db.QueryFirstOrDefaultAsync<BaleBotSettings>(sql);
        if (row == null)
        {
            return new BaleBotSettings
            {
                SettingsId = 1,
                IsEnabled = false,
                ApiBaseUrl = "https://tapi.bale.ai"
            };
        }

        row.BotToken = TryUnprotect(row.BotToken);
        row.WebhookSecret = TryUnprotect(row.WebhookSecret);
        if (string.IsNullOrWhiteSpace(row.ApiBaseUrl))
            row.ApiBaseUrl = "https://tapi.bale.ai";

        return row;
    }

    public async Task SaveSettingsAsync(bool isEnabled, string? botToken, string? botUsername, string? apiBaseUrl, bool requireBaleConnectionForMembers)
    {
        await EnsureOpenAsync();

        var tokenToStore = string.IsNullOrWhiteSpace(botToken) ? null : _crypto.Protect(botToken.Trim());
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://tapi.bale.ai" : apiBaseUrl.Trim().TrimEnd('/');

        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.BaleBotSettings WHERE SettingsId = 1)
BEGIN
    UPDATE dbo.BaleBotSettings
    SET IsEnabled = @isEnabled,
        BotToken = @tokenToStore,
        BotUsername = @botUsername,
        ApiBaseUrl = @baseUrl,
        RequireBaleConnectionForMembers = @requireBale,
        UpdatedAt = GETUTCDATE()
    WHERE SettingsId = 1;
END
ELSE
BEGIN
    INSERT INTO dbo.BaleBotSettings (SettingsId, IsEnabled, BotToken, BotUsername, ApiBaseUrl, RequireBaleConnectionForMembers)
    VALUES (1, @isEnabled, @tokenToStore, @botUsername, @baseUrl, @requireBale);
END";

        await _db.ExecuteAsync(sql, new
        {
            isEnabled,
            tokenToStore,
            botUsername = string.IsNullOrWhiteSpace(botUsername) ? null : botUsername.Trim().TrimStart('@'),
            baseUrl,
            requireBale = requireBaleConnectionForMembers
        });
    }

    public async Task UpdateBotUsernameAsync(string? botUsername)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.BaleBotSettings
SET BotUsername = @botUsername,
    UpdatedAt = GETUTCDATE()
WHERE SettingsId = 1;";

        await _db.ExecuteAsync(sql, new
        {
            botUsername = string.IsNullOrWhiteSpace(botUsername) ? null : botUsername.Trim().TrimStart('@')
        });
    }

    public async Task<string> EnsureWebhookSecretAsync()
    {
        await EnsureOpenAsync();

        const string selectSql = "SELECT TOP 1 WebhookSecret FROM dbo.BaleBotSettings WHERE SettingsId = 1";
        var stored = await _db.QueryFirstOrDefaultAsync<string?>(selectSql);
        var existing = TryUnprotect(stored);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var encrypted = _crypto.Protect(secret);

        const string sql = @"
UPDATE dbo.BaleBotSettings
SET WebhookSecret = @encrypted,
    UpdatedAt = GETUTCDATE()
WHERE SettingsId = 1;";

        await _db.ExecuteAsync(sql, new { encrypted });
        return secret;
    }

    public async Task<string?> GetWebhookSecretAsync()
    {
        await EnsureOpenAsync();
        const string sql = "SELECT TOP 1 WebhookSecret FROM dbo.BaleBotSettings WHERE SettingsId = 1";
        var stored = await _db.QueryFirstOrDefaultAsync<string?>(sql);
        return TryUnprotect(stored);
    }

    public async Task<string> CreateLinkTokenAsync(long memberId, TimeSpan lifetime)
    {
        await EnsureOpenAsync();

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var expiresAt = DateTime.UtcNow.Add(lifetime);

        const string sql = @"
UPDATE dbo.BaleLinkTokens
SET ExpiresAt = GETUTCDATE()
WHERE MemberId = @memberId
  AND UsedAt IS NULL
  AND ExpiresAt > GETUTCDATE();

INSERT INTO dbo.BaleLinkTokens (Token, MemberId, ExpiresAt)
VALUES (@token, @memberId, @expiresAt);";

        await _db.ExecuteAsync(sql, new { token, memberId, expiresAt });
        return token;
    }

    public async Task<long?> ConsumeLinkTokenAsync(string token, long chatId)
    {
        await EnsureOpenAsync();

        const string sql = @"
DECLARE @consumed TABLE (MemberId BIGINT);

UPDATE dbo.BaleLinkTokens
SET UsedAt = GETUTCDATE(),
    UsedChatId = @chatId
OUTPUT INSERTED.MemberId INTO @consumed
WHERE Token = @token
  AND UsedAt IS NULL
  AND ExpiresAt > GETUTCDATE();

SELECT TOP 1 MemberId FROM @consumed;";

        return await _db.QueryFirstOrDefaultAsync<long?>(sql, new { token, chatId });
    }

    public async Task<BaleSubscriber?> GetActiveByMemberAsync(long memberId)
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT TOP 1 s.*,
       m.FirstName AS MemberFirstName,
       m.LastName AS MemberLastName,
       m.MobileNumber AS MemberMobileNumber
FROM dbo.BaleSubscribers s
LEFT JOIN dbo.Members m ON m.MemberId = s.MemberId
WHERE s.MemberId = @memberId AND s.IsActive = 1
ORDER BY s.SubscriberId DESC;";

        return await _db.QueryFirstOrDefaultAsync<BaleSubscriber>(sql, new { memberId });
    }

    public async Task<IReadOnlyList<BaleSubscriber>> GetActiveByChatListAsync(long chatId)
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT s.*,
       m.FirstName AS MemberFirstName,
       m.LastName AS MemberLastName,
       m.MobileNumber AS MemberMobileNumber
FROM dbo.BaleSubscribers s
LEFT JOIN dbo.Members m ON m.MemberId = s.MemberId
WHERE s.ChatId = @chatId AND s.IsActive = 1
ORDER BY s.SubscriberId;";

        var rows = await _db.QueryAsync<BaleSubscriber>(sql, new { chatId });
        return rows.ToList();
    }

    public async Task<BaleSubscriber?> UpsertSubscriberAsync(
        long memberId,
        long chatId,
        long? baleUserId,
        string? username,
        string? firstName,
        string? lastName)
    {
        await EnsureOpenAsync();

        const string sql = @"
UPDATE dbo.BaleSubscribers
SET IsActive = 0,
    OptedOutAt = GETUTCDATE()
WHERE IsActive = 1
  AND MemberId = @memberId;

INSERT INTO dbo.BaleSubscribers
    (MemberId, ChatId, BaleUserId, Username, FirstName, LastName, IsActive, LinkedAt, LastSeenAt)
VALUES
    (@memberId, @chatId, @baleUserId, @username, @firstName, @lastName, 1, GETUTCDATE(), GETUTCDATE());

SELECT TOP 1 *
FROM dbo.BaleSubscribers
WHERE SubscriberId = CAST(SCOPE_IDENTITY() AS BIGINT);";

        try
        {
            return await _db.QueryFirstOrDefaultAsync<BaleSubscriber>(sql, new
            {
                memberId,
                chatId,
                baleUserId,
                username,
                firstName,
                lastName
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return await GetActiveByMemberAsync(memberId);
        }
    }

    public async Task<IReadOnlyList<BaleSubscriber>> UpsertPhoneSubscribersAsync(
        IReadOnlyCollection<long> memberIds,
        long chatId,
        long? baleUserId,
        string? username,
        string? firstName,
        string? lastName)
    {
        await EnsureOpenAsync();

        var ids = memberIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<BaleSubscriber>();

        using var tx = _db.BeginTransaction();
        try
        {
            const string deactivateSql = @"
UPDATE dbo.BaleSubscribers
SET IsActive = 0,
    OptedOutAt = GETUTCDATE()
WHERE IsActive = 1
  AND (MemberId IN @ids OR ChatId = @chatId);";

            await _db.ExecuteAsync(deactivateSql, new { ids, chatId }, tx);

            const string insertSql = @"
INSERT INTO dbo.BaleSubscribers
    (MemberId, ChatId, BaleUserId, Username, FirstName, LastName, IsActive, LinkedAt, LastSeenAt)
VALUES
    (@memberId, @chatId, @baleUserId, @username, @firstName, @lastName, 1, GETUTCDATE(), GETUTCDATE());

SELECT TOP 1 *
FROM dbo.BaleSubscribers
WHERE SubscriberId = CAST(SCOPE_IDENTITY() AS BIGINT);";

            var inserted = new List<BaleSubscriber>(ids.Length);
            foreach (var id in ids)
            {
                var row = await _db.QueryFirstOrDefaultAsync<BaleSubscriber>(insertSql, new
                {
                    memberId = id,
                    chatId,
                    baleUserId,
                    username,
                    firstName,
                    lastName
                }, tx);

                if (row != null)
                    inserted.Add(row);
            }

            tx.Commit();
            return inserted;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<bool> DeactivateByMemberAsync(long memberId)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.BaleSubscribers
SET IsActive = 0,
    OptedOutAt = GETUTCDATE()
WHERE MemberId = @memberId AND IsActive = 1;";

        return await _db.ExecuteAsync(sql, new { memberId }) > 0;
    }

    public async Task TouchSubscriberAsync(long chatId)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.BaleSubscribers
SET LastSeenAt = GETUTCDATE()
WHERE ChatId = @chatId AND IsActive = 1;";

        await _db.ExecuteAsync(sql, new { chatId });
    }

    public async Task<IReadOnlyList<BaleSubscriber>> ListAsync(string? query, bool onlyActive, int take = 300)
    {
        await EnsureOpenAsync();

        var sql = new StringBuilder(@"
SELECT TOP (@take)
    s.*,
    m.FirstName AS MemberFirstName,
    m.LastName AS MemberLastName,
    m.MobileNumber AS MemberMobileNumber
FROM dbo.BaleSubscribers s
LEFT JOIN dbo.Members m ON m.MemberId = s.MemberId
WHERE 1=1");

        var p = new DynamicParameters();
        p.Add("take", take);

        if (onlyActive)
            sql.Append(" AND s.IsActive = 1");

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            var digits = new string(q.Where(char.IsDigit).ToArray());

            sql.Append(@"
 AND (
      CAST(s.MemberId AS NVARCHAR(50)) LIKE @qLike
   OR ISNULL(m.FirstName, N'') LIKE @qLike
   OR ISNULL(m.LastName, N'') LIKE @qLike
   OR ISNULL(m.MobileNumber, N'') LIKE @qLike
   OR ISNULL(s.Username, N'') LIKE @qLike
   OR (@qDigits <> '' AND CAST(s.ChatId AS NVARCHAR(50)) LIKE @qDigitsLike)
 )");

            p.Add("qLike", "%" + q + "%");
            p.Add("qDigits", digits);
            p.Add("qDigitsLike", "%" + digits + "%");
        }

        sql.Append(" ORDER BY s.IsActive DESC, s.LinkedAt DESC");

        var rows = await _db.QueryAsync<BaleSubscriber>(sql.ToString(), p);
        return rows.ToList();
    }

    public sealed class SubscriberStats
    {
        public int ActiveCount { get; set; }
        public int TotalCount { get; set; }
    }

    public async Task<SubscriberStats> GetStatsAsync()
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT
    ISNULL(SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END), 0) AS ActiveCount,
    COUNT(1) AS TotalCount
FROM dbo.BaleSubscribers;";

        return await _db.QueryFirstOrDefaultAsync<SubscriberStats>(sql) ?? new SubscriberStats();
    }

    public async Task<bool> TryMarkUpdateProcessedAsync(long updateId, long? chatId, string? messageText)
    {
        await EnsureOpenAsync();
        const string sql = @"
INSERT INTO dbo.BaleUpdates (UpdateId, ChatId, MessageText)
SELECT @updateId, @chatId, @messageText
WHERE NOT EXISTS (SELECT 1 FROM dbo.BaleUpdates WHERE UpdateId = @updateId);";

        try
        {
            return await _db.ExecuteAsync(sql, new { updateId, chatId, messageText }) > 0;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    public async Task<long> CreateMessageLogAsync(
        long? notificationId,
        long? memberId,
        long? chatId,
        string? featureKey,
        string? messageText)
    {
        await EnsureOpenAsync();
        const string sql = @"
INSERT INTO dbo.BaleMessageLogs (NotificationId, MemberId, ChatId, FeatureKey, MessageText)
OUTPUT INSERTED.LogId
VALUES (@notificationId, @memberId, @chatId, @featureKey, @messageText);";

        return await _db.QuerySingleAsync<long>(sql, new
        {
            notificationId,
            memberId,
            chatId,
            featureKey,
            messageText
        });
    }

    public async Task MarkMessageResultAsync(
        long logId,
        bool isSuccess,
        string? rawResponse,
        long? providerMessageId,
        int? apiStatus,
        string? apiMessage,
        string? errorMessage)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.BaleMessageLogs
SET IsSuccess = @isSuccess,
    RawResponse = @rawResponse,
    ProviderMessageId = @providerMessageId,
    ApiStatus = @apiStatus,
    ApiMessage = @apiMessage,
    ErrorMessage = @errorMessage,
    SentAt = GETUTCDATE()
WHERE LogId = @logId;";

        await _db.ExecuteAsync(sql, new
        {
            logId,
            isSuccess,
            rawResponse,
            providerMessageId,
            apiStatus,
            apiMessage,
            errorMessage
        });
    }

    public async Task<int> GetUnreadMessageCountAsync(long memberId)
    {
        await EnsureOpenAsync();
        const string sql = @"
SELECT COUNT(1)
FROM dbo.BaleMessageLogs
WHERE MemberId = @memberId
  AND IsSuccess = 1
  AND ReadAt IS NULL;";

        return await _db.ExecuteScalarAsync<int>(sql, new { memberId });
    }

    public async Task<IReadOnlyList<BaleMessageLog>> GetMemberInboxAsync(long memberId, bool onlyUnread, int take = 100)
    {
        await EnsureOpenAsync();

        var sql = new StringBuilder(@"
SELECT TOP (@take)
    LogId, NotificationId, MemberId, ChatId, FeatureKey, MessageText, ProviderMessageId,
    ApiStatus, ApiMessage, IsSuccess, ErrorMessage, CreatedAt, SentAt, ReadAt
FROM dbo.BaleMessageLogs
WHERE MemberId = @memberId
  AND IsSuccess = 1");

        if (onlyUnread)
            sql.Append(" AND ReadAt IS NULL");

        sql.Append(" ORDER BY CreatedAt DESC, LogId DESC");

        var rows = await _db.QueryAsync<BaleMessageLog>(sql.ToString(), new { memberId, take });
        return rows.ToList();
    }

    public async Task<bool> MarkMessageReadAsync(long memberId, long logId)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.BaleMessageLogs
SET ReadAt = GETUTCDATE()
WHERE LogId = @logId
  AND MemberId = @memberId
  AND ReadAt IS NULL;";

        return await _db.ExecuteAsync(sql, new { logId, memberId }) > 0;
    }

    public async Task<int> MarkAllMessagesReadAsync(long memberId)
    {
        await EnsureOpenAsync();
        const string sql = @"
UPDATE dbo.BaleMessageLogs
SET ReadAt = GETUTCDATE()
WHERE MemberId = @memberId
  AND IsSuccess = 1
  AND ReadAt IS NULL;";

        return await _db.ExecuteAsync(sql, new { memberId });
    }

    public async Task<IReadOnlyList<BaleMessageLogReportRow>> SearchMessageLogsAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? query,
        bool? isSuccess,
        bool? successIsNull,
        int take = 300)
    {
        await EnsureOpenAsync();

        var p = new DynamicParameters();
        p.Add("take", take);
        var where = BuildMessageLogWhere(p, fromUtc, toUtc, query, isSuccess, successIsNull);

        var sql = new StringBuilder(@"
SELECT TOP (@take)
    l.LogId, l.NotificationId, l.MemberId, l.ChatId, l.FeatureKey, l.MessageText,
    l.ProviderMessageId, l.ApiStatus, l.ApiMessage, l.IsSuccess, l.ErrorMessage, l.RawResponse,
    l.CreatedAt, l.SentAt,
    m.FirstName AS MemberFirstName,
    m.LastName AS MemberLastName,
    m.MobileNumber AS MemberMobileNumber,
    bs.Username AS BaleUsername,
    bs.LastSeenAt AS SubscriberLastSeenAt
FROM dbo.BaleMessageLogs l
LEFT JOIN dbo.Members m ON m.MemberId = l.MemberId
OUTER APPLY (
    SELECT TOP 1 s.Username, s.LastSeenAt
    FROM dbo.BaleSubscribers s
    WHERE s.ChatId = l.ChatId
      AND (l.MemberId IS NULL OR s.MemberId = l.MemberId)
    ORDER BY CASE WHEN s.IsActive = 1 THEN 0 ELSE 1 END, s.SubscriberId DESC
) bs
WHERE 1=1");
        sql.Append(where);
        sql.Append(" ORDER BY l.CreatedAt DESC, l.LogId DESC");

        var rows = await _db.QueryAsync<BaleMessageLogReportRow>(sql.ToString(), p);
        return rows.ToList();
    }

    public async Task<BaleMessageLogReportStats> GetMessageLogReportStatsAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? query,
        bool? isSuccess,
        bool? successIsNull)
    {
        await EnsureOpenAsync();

        var p = new DynamicParameters();
        var where = BuildMessageLogWhere(p, fromUtc, toUtc, query, isSuccess, successIsNull);

        var sql = new StringBuilder(@"
SELECT
    COUNT(1) AS Total,
    ISNULL(SUM(CASE WHEN l.IsSuccess = 1 THEN 1 ELSE 0 END), 0) AS Success,
    ISNULL(SUM(CASE WHEN l.IsSuccess = 0 THEN 1 ELSE 0 END), 0) AS Failed,
    ISNULL(SUM(CASE WHEN l.IsSuccess IS NULL THEN 1 ELSE 0 END), 0) AS Pending,
    COUNT(DISTINCT l.MemberId) AS UniqueMembers,
    ISNULL(SUM(CASE WHEN l.SentAt IS NOT NULL AND bs.LastSeenAt IS NOT NULL AND bs.LastSeenAt > l.SentAt THEN 1 ELSE 0 END), 0) AS ActivityAfterSend
FROM dbo.BaleMessageLogs l
LEFT JOIN dbo.Members m ON m.MemberId = l.MemberId
OUTER APPLY (
    SELECT TOP 1 s.LastSeenAt
    FROM dbo.BaleSubscribers s
    WHERE s.ChatId = l.ChatId
      AND (l.MemberId IS NULL OR s.MemberId = l.MemberId)
    ORDER BY CASE WHEN s.IsActive = 1 THEN 0 ELSE 1 END, s.SubscriberId DESC
) bs
WHERE 1=1");
        sql.Append(where);

        return await _db.QueryFirstOrDefaultAsync<BaleMessageLogReportStats>(sql.ToString(), p)
               ?? new BaleMessageLogReportStats();
    }

    private static string BuildMessageLogWhere(
        DynamicParameters p,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? query,
        bool? isSuccess,
        bool? successIsNull)
    {
        var where = new StringBuilder();

        if (fromUtc.HasValue)
        {
            where.Append(" AND l.CreatedAt >= @fromUtc");
            p.Add("fromUtc", fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            where.Append(" AND l.CreatedAt < DATEADD(DAY, 1, @toUtc)");
            p.Add("toUtc", toUtc.Value);
        }

        if (successIsNull == true)
            where.Append(" AND l.IsSuccess IS NULL");
        else if (isSuccess.HasValue)
        {
            where.Append(" AND l.IsSuccess = @isSuccess");
            p.Add("isSuccess", isSuccess.Value);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            var digits = new string(q.Where(char.IsDigit).ToArray());

            where.Append(@"
 AND (
      ISNULL(m.FirstName, N'') LIKE @qLike
   OR ISNULL(m.LastName, N'') LIKE @qLike
   OR ISNULL(m.MobileNumber, N'') LIKE @qLike
   OR ISNULL(bs.Username, N'') LIKE @qLike
   OR ISNULL(l.FeatureKey, N'') LIKE @qLike
   OR ISNULL(l.MessageText, N'') LIKE @qLike
   OR (@qDigits <> '' AND CAST(l.MemberId AS NVARCHAR(50)) LIKE @qDigitsLike)
   OR (@qDigits <> '' AND CAST(l.ChatId AS NVARCHAR(50)) LIKE @qDigitsLike)
   OR (@qDigits <> '' AND CAST(l.NotificationId AS NVARCHAR(50)) LIKE @qDigitsLike)
 )");

            p.Add("qLike", "%" + q + "%");
            p.Add("qDigits", digits);
            p.Add("qDigitsLike", "%" + digits + "%");
        }

        return where.ToString();
    }

    private string? TryUnprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        try
        {
            return _crypto.Unprotect(value);
        }
        catch
        {
            return value;
        }
    }
}
