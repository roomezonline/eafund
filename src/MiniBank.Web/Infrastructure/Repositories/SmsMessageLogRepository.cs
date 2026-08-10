using Dapper;
using MiniBank.Web.Models.Sms;
using System.Data;
using System.Data.Common;
using System.Text;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class SmsMessageLogRepository
    {
        private readonly IDbConnection _db;

        public SmsMessageLogRepository(IDbConnection db)
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

        public async Task<long> CreateAsync(
            string? featureKey,
            long? householdId,
            long? headMemberId,
            string? memberIdsJson,
            string? recipientMobile,
            int? templateId,
            string? paramsJson,
            string? messageText)
        {
            await EnsureOpenAsync();
            const string sql = @"
INSERT INTO dbo.SmsMessageLogs
(
    FeatureKey,
    HouseholdId,
    HeadMemberId,
    MemberIdsJson,
    RecipientMobile,
    TemplateId,
    ParamsJson,
    MessageText
)
OUTPUT INSERTED.LogId
VALUES
(
    @featureKey,
    @householdId,
    @headMemberId,
    @memberIdsJson,
    @recipientMobile,
    @templateId,
    @paramsJson,
    @messageText
);";

            return await _db.QuerySingleAsync<long>(sql, new
            {
                featureKey,
                householdId,
                headMemberId,
                memberIdsJson,
                recipientMobile,
                templateId,
                paramsJson,
                messageText
            });
        }

        public async Task MarkResultAsync(
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
UPDATE dbo.SmsMessageLogs
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

        public async Task UpdateDeliveryAsync(long logId, byte? deliveryState, DateTime? deliveryDateTime)
        {
            await EnsureOpenAsync();
            const string sql = @"
UPDATE dbo.SmsMessageLogs
SET DeliveryState = @deliveryState,
    DeliveryDateTime = @deliveryDateTime
WHERE LogId = @logId;";

            await _db.ExecuteAsync(sql, new { logId, deliveryState, deliveryDateTime });
        }

        public async Task UpdateDeliveryAsync(long logId, byte? deliveryState, DateTime? deliveryDateTime, int? apiStatus, string? apiMessage)
        {
            await EnsureOpenAsync();
            const string sql = @"
UPDATE dbo.SmsMessageLogs
SET DeliveryState = @deliveryState,
    DeliveryDateTime = @deliveryDateTime,
    ApiStatus = @apiStatus,
    ApiMessage = @apiMessage
WHERE LogId = @logId;";

            await _db.ExecuteAsync(sql, new { logId, deliveryState, deliveryDateTime, apiStatus, apiMessage });
        }

        public async Task<IReadOnlyList<SmsMessageLog>> SearchAsync(
            DateTime? fromUtc,
            DateTime? toUtc,
            string? recipientMobile,
            string? featureKey,
            long? headMemberId,
            long? householdId,
            bool? isSuccess,
            int take = 200)
        {
            await EnsureOpenAsync();

            var sql = @"
SELECT TOP (@take)
    LogId,
    FeatureKey,
    HouseholdId,
    HeadMemberId,
    MemberIdsJson,
    RecipientMobile,
    TemplateId,
    ParamsJson,
    MessageText,
    ProviderMessageId,
    ApiStatus,
    ApiMessage,
    DeliveryState,
    DeliveryDateTime,
    IsSuccess,
    ErrorMessage,
    RawResponse,
    CreatedAt,
    SentAt
FROM dbo.SmsMessageLogs
WHERE 1=1";

            var p = new DynamicParameters();
            p.Add("take", take);

            if (fromUtc.HasValue)
            {
                sql += " AND CreatedAt >= @fromUtc";
                p.Add("fromUtc", fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                sql += " AND CreatedAt < DATEADD(DAY, 1, @toUtc)";
                p.Add("toUtc", toUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(recipientMobile))
            {
                sql += " AND RecipientMobile LIKE @recipientMobile";
                p.Add("recipientMobile", "%" + recipientMobile.Trim() + "%");
            }

            if (!string.IsNullOrWhiteSpace(featureKey))
            {
                sql += " AND FeatureKey = @featureKey";
                p.Add("featureKey", featureKey.Trim());
            }

            if (headMemberId.HasValue && headMemberId.Value > 0)
            {
                sql += " AND HeadMemberId = @headMemberId";
                p.Add("headMemberId", headMemberId.Value);
            }

            if (householdId.HasValue && householdId.Value > 0)
            {
                sql += " AND HouseholdId = @householdId";
                p.Add("householdId", householdId.Value);
            }

            if (isSuccess.HasValue)
            {
                sql += " AND IsSuccess = @isSuccess";
                p.Add("isSuccess", isSuccess.Value);
            }

            sql += " ORDER BY CreatedAt DESC";

            var rows = await _db.QueryAsync<SmsMessageLog>(sql, p);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<SmsMessageLogReportRow>> SearchReportAsync(
            DateTime? fromUtc,
            DateTime? toUtc,
            string? query,
            byte? deliveryState,
            bool? deliveryIsNull,
            int take = 200)
        {
            await EnsureOpenAsync();

            var sql = new StringBuilder(@"
SELECT TOP (@take)
    l.LogId,
    l.FeatureKey,
    l.HouseholdId,
    l.HeadMemberId,
    l.MemberIdsJson,
    l.RecipientMobile,
    l.TemplateId,
    l.ParamsJson,
    l.MessageText,
    l.ProviderMessageId,
    l.ApiStatus,
    l.ApiMessage,
    l.DeliveryState,
    l.DeliveryDateTime,
    l.IsSuccess,
    l.ErrorMessage,
    l.RawResponse,
    l.CreatedAt,
    l.SentAt,
    LTRIM(RTRIM(CONCAT(ISNULL(m.FirstName, N''), N' ', ISNULL(m.LastName, N'')))) AS HeadFullName,
    m.MobileNumber AS HeadMobileNumber,
    m.HouseholdCode AS HeadHouseholdCode
FROM dbo.SmsMessageLogs l
LEFT JOIN dbo.Members m ON m.MemberId = l.HeadMemberId
WHERE 1=1");

            var p = new DynamicParameters();
            p.Add("take", take);

            if (fromUtc.HasValue)
            {
                sql.Append(" AND l.CreatedAt >= @fromUtc");
                p.Add("fromUtc", fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                sql.Append(" AND l.CreatedAt < DATEADD(DAY, 1, @toUtc)");
                p.Add("toUtc", toUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                var digits = new string(q.Where(char.IsDigit).ToArray());

                sql.Append(@"
 AND (
      l.RecipientMobile LIKE @qLike
   OR LTRIM(RTRIM(CONCAT(ISNULL(m.FirstName, N''), N' ', ISNULL(m.LastName, N'')))) LIKE @qLike
   OR (@qDigits <> '' AND (CAST(l.HeadMemberId AS NVARCHAR(50)) LIKE @qDigitsLike OR m.MobileNumber LIKE @qDigitsLike))
 )");

                p.Add("qLike", "%" + q + "%");
                p.Add("qDigits", digits);
                p.Add("qDigitsLike", "%" + digits + "%");
            }

            if (deliveryIsNull.HasValue && deliveryIsNull.Value)
            {
                sql.Append(" AND l.DeliveryState IS NULL");
            }
            else if (deliveryState.HasValue)
            {
                sql.Append(" AND l.DeliveryState = @deliveryState");
                p.Add("deliveryState", deliveryState.Value);
            }

            sql.Append(" ORDER BY l.CreatedAt DESC");

            var rows = await _db.QueryAsync<SmsMessageLogReportRow>(sql.ToString(), p);
            return rows.ToList();
        }

        public sealed class ReminderSummaryRow
        {
            public long InstallmentId { get; set; }
            public int SendCount { get; set; }
            public DateTime? LastSentAt { get; set; }
            public long? LastLogId { get; set; }
        }

        public async Task<IReadOnlyList<ReminderSummaryRow>> GetLoanInstallmentReminderSummaryAsync(IEnumerable<long> installmentIds, string featureKey)
        {
            await EnsureOpenAsync();

            var ids = (installmentIds ?? Array.Empty<long>()).Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return Array.Empty<ReminderSummaryRow>();

            const string sql = @"
SELECT
    CAST(JSON_VALUE(ParamsJson, '$.InstallmentId') AS BIGINT) AS InstallmentId,
    COUNT(1) AS SendCount,
    MAX(SentAt) AS LastSentAt,
    MAX(LogId) AS LastLogId
FROM dbo.SmsMessageLogs
WHERE FeatureKey = @featureKey
  AND JSON_VALUE(ParamsJson, '$.InstallmentId') IS NOT NULL
  AND CAST(JSON_VALUE(ParamsJson, '$.InstallmentId') AS BIGINT) IN @ids
GROUP BY CAST(JSON_VALUE(ParamsJson, '$.InstallmentId') AS BIGINT);";

            var rows = await _db.QueryAsync<ReminderSummaryRow>(sql, new { ids, featureKey });
            return rows.ToList();
        }

        public async Task<IReadOnlyList<SmsMessageLog>> GetLoanInstallmentReminderLogsAsync(long installmentId, string featureKey, int take = 50)
        {
            await EnsureOpenAsync();
            if (installmentId <= 0)
                return Array.Empty<SmsMessageLog>();

            const string sql = @"
SELECT TOP (@take)
    LogId,
    FeatureKey,
    HouseholdId,
    HeadMemberId,
    MemberIdsJson,
    RecipientMobile,
    TemplateId,
    ParamsJson,
    MessageText,
    ProviderMessageId,
    ApiStatus,
    ApiMessage,
    DeliveryState,
    DeliveryDateTime,
    IsSuccess,
    ErrorMessage,
    RawResponse,
    CreatedAt,
    SentAt
FROM dbo.SmsMessageLogs
WHERE FeatureKey = @featureKey
  AND CAST(JSON_VALUE(ParamsJson, '$.InstallmentId') AS BIGINT) = @installmentId
ORDER BY CreatedAt DESC;";

            var rows = await _db.QueryAsync<SmsMessageLog>(sql, new { take, installmentId, featureKey });
            return rows.ToList();
        }
    }
}
