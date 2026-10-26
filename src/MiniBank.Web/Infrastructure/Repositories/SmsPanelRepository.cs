using Dapper;
using MiniBank.Web.Models.Sms;
using System.Data;
using System.Data.Common;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class SmsPanelRepository
    {
        private readonly IDbConnection _db;

        public SmsPanelRepository(IDbConnection db)
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

        public async Task<SmsSettings> GetSettingsAsync()
        {
            await EnsureOpenAsync();
            const string sql = @"SELECT TOP 1 SettingsId, IsEnabled, ProviderName, VerifyTemplateId, CreditRial, PartCostRial, LowCreditThresholdRial, CreatedAt, UpdatedAt FROM dbo.SmsSettings WHERE SettingsId = 1";
            var row = await _db.QueryFirstOrDefaultAsync<SmsSettings>(sql);
            return row ?? new SmsSettings { SettingsId = 1, IsEnabled = false };
        }

        public async Task UpsertSettingsAsync(bool isEnabled, string? providerName, string? verifyTemplateId, long creditRial, int partCostRial, long lowCreditThresholdRial)
        {
            await EnsureOpenAsync();
            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.SmsSettings WHERE SettingsId = 1)
BEGIN
    UPDATE dbo.SmsSettings
    SET IsEnabled = @isEnabled,
        ProviderName = @providerName,
        VerifyTemplateId = @verifyTemplateId,
        CreditRial = @creditRial,
        PartCostRial = @partCostRial,
        LowCreditThresholdRial = @lowCreditThresholdRial,
        UpdatedAt = GETUTCDATE()
    WHERE SettingsId = 1;
END
ELSE
BEGIN
    INSERT INTO dbo.SmsSettings (SettingsId, IsEnabled, ProviderName, VerifyTemplateId, CreditRial, PartCostRial, LowCreditThresholdRial)
    VALUES (1, @isEnabled, @providerName, @verifyTemplateId, @creditRial, @partCostRial, @lowCreditThresholdRial);
END";
            await _db.ExecuteAsync(sql, new { isEnabled, providerName, verifyTemplateId, creditRial, partCostRial, lowCreditThresholdRial });
        }

        public async Task<(bool ok, long spentRial, long remainingRial, bool lowCredit, long thresholdRial)> ConsumeCreditForPartsAsync(int parts)
        {
            await EnsureOpenAsync();

            if (parts <= 0)
                parts = 1;

            const string sql = @"
DECLARE @partCost INT = (SELECT TOP 1 ISNULL(PartCostRial, 0) FROM dbo.SmsSettings WHERE SettingsId = 1);
DECLARE @threshold BIGINT = (SELECT TOP 1 ISNULL(LowCreditThresholdRial, 0) FROM dbo.SmsSettings WHERE SettingsId = 1);
DECLARE @delta BIGINT = CAST(@partCost AS BIGINT) * @parts;

-- If part cost is zero, don't consume (but still return current values)
IF (@delta <= 0)
BEGIN
    SELECT
        CAST(1 AS BIT) AS Ok,
        CAST(0 AS BIGINT) AS SpentRial,
        (SELECT TOP 1 ISNULL(CreditRial, 0) FROM dbo.SmsSettings WHERE SettingsId = 1) AS RemainingRial,
        CAST(CASE WHEN (SELECT TOP 1 ISNULL(CreditRial, 0) FROM dbo.SmsSettings WHERE SettingsId = 1) < @threshold THEN 1 ELSE 0 END AS BIT) AS LowCredit,
        @threshold AS ThresholdRial;
    RETURN;
END

UPDATE dbo.SmsSettings
SET CreditRial = CASE WHEN CreditRial >= @delta THEN CreditRial - @delta ELSE 0 END,
    UpdatedAt = GETUTCDATE()
WHERE SettingsId = 1;

DECLARE @remaining BIGINT = (SELECT TOP 1 ISNULL(CreditRial, 0) FROM dbo.SmsSettings WHERE SettingsId = 1);
SELECT
    CAST(CASE WHEN @remaining >= 0 THEN 1 ELSE 1 END AS BIT) AS Ok,
    @delta AS SpentRial,
    @remaining AS RemainingRial,
    CAST(CASE WHEN @remaining < @threshold THEN 1 ELSE 0 END AS BIT) AS LowCredit,
    @threshold AS ThresholdRial;";

            var row = await _db.QuerySingleAsync(sql, new { parts });
            bool ok = row.Ok;
            long spentRial = row.SpentRial;
            long remainingRial = row.RemainingRial;
            bool lowCredit = row.LowCredit;
            long thresholdRial = row.ThresholdRial;
            return (ok, spentRial, remainingRial, lowCredit, thresholdRial);
        }

        public async Task<IReadOnlyList<SmsCreditTopup>> GetTopupsAsync(int take = 50)
        {
            await EnsureOpenAsync();
            const string sql = @"SELECT TOP (@take) TopupId, AmountRial, Notes, CreatedAt FROM dbo.SmsCreditTopups ORDER BY CreatedAt DESC";
            var rows = await _db.QueryAsync<SmsCreditTopup>(sql, new { take });
            return rows.ToList();
        }

        public async Task AddTopupAsync(long amountRial, string? notes = null)
        {
            await EnsureOpenAsync();
            const string sql = @"
IF (@amountRial IS NULL OR @amountRial <= 0)
    RETURN;

UPDATE dbo.SmsSettings
SET CreditRial = ISNULL(CreditRial, 0) + @amountRial,
    UpdatedAt = GETUTCDATE()
WHERE SettingsId = 1;

INSERT INTO dbo.SmsCreditTopups (AmountRial, Notes)
VALUES (@amountRial, @notes);
";
            await _db.ExecuteAsync(sql, new { amountRial, notes });
        }

        public async Task DeleteTopupAsync(long topupId)
        {
            await EnsureOpenAsync();
            const string sql = @"DELETE FROM dbo.SmsCreditTopups WHERE TopupId = @topupId";
            await _db.ExecuteAsync(sql, new { topupId });
        }
    }
}
