using Dapper;
using MiniBank.Web.Models;
using System.Data;
using System.Data.Common;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class DepositRateTierRepository
    {
        private readonly IDbConnection _connection;

        public DepositRateTierRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public Task<IEnumerable<DepositRateTier>> GetByPlanIdAsync(long planId)
        {
            const string sql = "SELECT * FROM dbo.DepositRateTiers WHERE PlanId = @planId ORDER BY OrderNo ASC, TierId ASC";
            return _connection.QueryAsync<DepositRateTier>(sql, new { planId });
        }

        public Task<long> CreateAsync(DepositRateTier tier)
        {
            const string sql = @"INSERT INTO dbo.DepositRateTiers
(PlanId, OrderNo, FromAmount, ToAmount, RatePercent)
OUTPUT INSERTED.TierId
VALUES (@PlanId, @OrderNo, @FromAmount, @ToAmount, @RatePercent);";

            return _connection.QuerySingleAsync<long>(sql, tier);
        }

        public async Task ReplaceForPlanAsync(long planId, IEnumerable<DepositRateTier> tiers)
        {
            var openedHere = false;
            if (_connection.State != ConnectionState.Open)
            {
                if (_connection is DbConnection dbc)
                    await dbc.OpenAsync();
                else
                    _connection.Open();

                openedHere = true;
            }

            using var tx = _connection.BeginTransaction();
            try
            {
                await _connection.ExecuteAsync("DELETE FROM dbo.DepositRateTiers WHERE PlanId = @planId", new { planId }, tx);

                foreach (var t in tiers)
                {
                    await _connection.ExecuteAsync(@"INSERT INTO dbo.DepositRateTiers (PlanId, OrderNo, FromAmount, ToAmount, RatePercent)
VALUES (@PlanId, @OrderNo, @FromAmount, @ToAmount, @RatePercent);", new
                    {
                        PlanId = planId,
                        t.OrderNo,
                        t.FromAmount,
                        t.ToAmount,
                        t.RatePercent
                    }, tx);
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
            finally
            {
                if (openedHere)
                {
                    try { _connection.Close(); } catch { }
                }
            }
        }

        public Task DeleteAsync(long tierId)
        {
            const string sql = "DELETE FROM dbo.DepositRateTiers WHERE TierId = @tierId";
            return _connection.ExecuteAsync(sql, new { tierId });
        }
    }
}
