using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class LoanRatePlanRepository
    {
        private readonly IDbConnection _connection;

        public LoanRatePlanRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<IEnumerable<LoanRatePlan>> GetAllAsync(bool? isActive = null)
        {
            var sql = "SELECT * FROM dbo.LoanRatePlans WHERE 1=1";
            var p = new DynamicParameters();

            if (isActive.HasValue)
            {
                sql += " AND IsActive = @isActive";
                p.Add("isActive", isActive.Value);
            }

            sql += " ORDER BY EffectiveFrom DESC, PlanId DESC";
            return await _connection.QueryAsync<LoanRatePlan>(sql, p);
        }

        public Task<LoanRatePlan?> GetByIdAsync(long planId)
        {
            const string sql = "SELECT * FROM dbo.LoanRatePlans WHERE PlanId = @planId";
            return _connection.QueryFirstOrDefaultAsync<LoanRatePlan>(sql, new { planId });
        }

        public Task<LoanRatePlan?> GetByIdAsync(long planId, IDbTransaction? tx)
        {
            const string sql = "SELECT * FROM dbo.LoanRatePlans WHERE PlanId = @planId";
            return _connection.QueryFirstOrDefaultAsync<LoanRatePlan>(sql, new { planId }, tx);
        }

        public Task<LoanRatePlan?> GetActiveForDateAsync(DateTime date)
        {
            const string sql = @"SELECT TOP 1 *
FROM dbo.LoanRatePlans
WHERE IsActive = 1
  AND EffectiveFrom <= @d
  AND (EffectiveTo IS NULL OR EffectiveTo >= @d)
ORDER BY EffectiveFrom DESC, PlanId DESC;";

            return _connection.QueryFirstOrDefaultAsync<LoanRatePlan>(sql, new { d = date.Date });
        }

        public Task<LoanRatePlan?> GetActiveForDateAsync(DateTime date, IDbTransaction? tx)
        {
            const string sql = @"SELECT TOP 1 *
FROM dbo.LoanRatePlans
WHERE IsActive = 1
  AND EffectiveFrom <= @d
  AND (EffectiveTo IS NULL OR EffectiveTo >= @d)
ORDER BY EffectiveFrom DESC, PlanId DESC;";

            return _connection.QueryFirstOrDefaultAsync<LoanRatePlan>(sql, new { d = date.Date }, tx);
        }

        public Task<LoanRatePlan?> GetLatestActiveAsync()
        {
            const string sql = @"SELECT TOP 1 *
FROM dbo.LoanRatePlans
WHERE IsActive = 1
ORDER BY EffectiveFrom DESC, PlanId DESC;";

            return _connection.QueryFirstOrDefaultAsync<LoanRatePlan>(sql);
        }

        public Task<LoanRatePlan?> GetLatestActiveAsync(IDbTransaction? tx)
        {
            const string sql = @"SELECT TOP 1 *
FROM dbo.LoanRatePlans
WHERE IsActive = 1
ORDER BY EffectiveFrom DESC, PlanId DESC;";

            return _connection.QueryFirstOrDefaultAsync<LoanRatePlan>(sql, transaction: tx);
        }

        public Task<long> CreateAsync(LoanRatePlan plan)
        {
            const string sql = @"INSERT INTO dbo.LoanRatePlans
(EffectiveFrom, EffectiveTo, AnnualRatePercent, PenaltyAnnualRatePercent, Notes, IsActive, CreatedAt)
OUTPUT INSERTED.PlanId
VALUES (@EffectiveFrom, @EffectiveTo, @AnnualRatePercent, @PenaltyAnnualRatePercent, @Notes, @IsActive, GETUTCDATE());";

            return _connection.QuerySingleAsync<long>(sql, new
            {
                EffectiveFrom = plan.EffectiveFrom.Date,
                EffectiveTo = plan.EffectiveTo?.Date,
                plan.AnnualRatePercent,
                plan.PenaltyAnnualRatePercent,
                plan.Notes,
                plan.IsActive
            });
        }

        public Task UpdateAsync(LoanRatePlan plan)
        {
            const string sql = @"UPDATE dbo.LoanRatePlans
SET EffectiveFrom = @EffectiveFrom,
    EffectiveTo = @EffectiveTo,
    AnnualRatePercent = @AnnualRatePercent,
    PenaltyAnnualRatePercent = @PenaltyAnnualRatePercent,
    Notes = @Notes,
    IsActive = @IsActive
WHERE PlanId = @PlanId;";

            return _connection.ExecuteAsync(sql, new
            {
                PlanId = plan.PlanId,
                EffectiveFrom = plan.EffectiveFrom.Date,
                EffectiveTo = plan.EffectiveTo?.Date,
                plan.AnnualRatePercent,
                plan.PenaltyAnnualRatePercent,
                plan.Notes,
                plan.IsActive
            });
        }

        public Task DeleteAsync(long planId)
        {
            const string sql = "DELETE FROM dbo.LoanRatePlans WHERE PlanId = @planId";
            return _connection.ExecuteAsync(sql, new { planId });
        }
    }
}
