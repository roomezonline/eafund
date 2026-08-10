using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class DepositRatePlanRepository
    {
        private readonly IDbConnection _connection;

        public DepositRatePlanRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<IEnumerable<DepositRatePlan>> GetAllAsync(bool? isActive = null)
        {
            var sql = "SELECT * FROM dbo.DepositRatePlans WHERE 1=1";
            var p = new DynamicParameters();

            if (isActive.HasValue)
            {
                sql += " AND IsActive = @isActive";
                p.Add("isActive", isActive.Value);
            }

            sql += " ORDER BY EffectiveFrom DESC, PlanId DESC";
            return await _connection.QueryAsync<DepositRatePlan>(sql, p);
        }

        public Task<DepositRatePlan?> GetByIdAsync(long planId)
        {
            const string sql = "SELECT * FROM dbo.DepositRatePlans WHERE PlanId = @planId";
            return _connection.QueryFirstOrDefaultAsync<DepositRatePlan>(sql, new { planId });
        }

        public Task<long> CreateAsync(DepositRatePlan plan)
        {
            const string sql = @"INSERT INTO dbo.DepositRatePlans
(Mode, EffectiveFrom, EffectiveTo, SingleRatePercent, Notes, IsActive, CreatedAt)
OUTPUT INSERTED.PlanId
VALUES (@Mode, @EffectiveFrom, @EffectiveTo, @SingleRatePercent, @Notes, @IsActive, GETUTCDATE());";

            return _connection.QuerySingleAsync<long>(sql, new
            {
                Mode = (byte)plan.Mode,
                EffectiveFrom = plan.EffectiveFrom.Date,
                EffectiveTo = plan.EffectiveTo?.Date,
                plan.SingleRatePercent,
                plan.Notes,
                plan.IsActive
            });
        }

        public Task UpdateAsync(DepositRatePlan plan)
        {
            const string sql = @"UPDATE dbo.DepositRatePlans
SET Mode = @Mode,
    EffectiveFrom = @EffectiveFrom,
    EffectiveTo = @EffectiveTo,
    SingleRatePercent = @SingleRatePercent,
    Notes = @Notes,
    IsActive = @IsActive
WHERE PlanId = @PlanId;";

            return _connection.ExecuteAsync(sql, new
            {
                PlanId = plan.PlanId,
                Mode = (byte)plan.Mode,
                EffectiveFrom = plan.EffectiveFrom.Date,
                EffectiveTo = plan.EffectiveTo?.Date,
                plan.SingleRatePercent,
                plan.Notes,
                plan.IsActive
            });
        }

        public Task DeleteAsync(long planId)
        {
            const string sql = "DELETE FROM dbo.DepositRatePlans WHERE PlanId = @planId";
            return _connection.ExecuteAsync(sql, new { planId });
        }
    }
}
