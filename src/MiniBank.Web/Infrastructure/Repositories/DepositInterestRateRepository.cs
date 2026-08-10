using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class DepositInterestRateRepository
    {
        protected readonly IDbConnection Connection;
        public DepositInterestRateRepository(IDbConnection connection) { Connection = connection; }

        public async Task<IEnumerable<DepositInterestRate>> GetByDepositTypeAsync(long depositTypeId)
        {
            var sql = @"SELECT * FROM DepositInterestRates WHERE DepositTypeId=@depositTypeId ORDER BY EffectiveFrom DESC, CreatedAt DESC";
            return await Connection.QueryAsync<DepositInterestRate>(sql, new { depositTypeId });
        }

        public async Task<long> CreateAsync(DepositInterestRate model)
        {
            var sql = @"INSERT INTO DepositInterestRates (DepositTypeId, InterestRate, EffectiveFrom, EffectiveTo, Notes, CreatedAt)
                         OUTPUT INSERTED.RateId
                         VALUES (@DepositTypeId, @InterestRate, @EffectiveFrom, @EffectiveTo, @Notes, GETUTCDATE())";
            return await Connection.QuerySingleAsync<long>(sql, model);
        }

        public async Task UpdateAsync(DepositInterestRate model)
        {
            var sql = @"UPDATE DepositInterestRates
                        SET InterestRate=@InterestRate, EffectiveFrom=@EffectiveFrom, EffectiveTo=@EffectiveTo, Notes=@Notes
                        WHERE RateId=@RateId";
            await Connection.ExecuteAsync(sql, model);
        }

        public async Task DeleteAsync(long rateId)
        {
            await Connection.ExecuteAsync("DELETE FROM DepositInterestRates WHERE RateId=@rateId", new { rateId });
        }

        // Bulk load rates for multiple deposit types (performance optimization)
        public async Task<IEnumerable<DepositInterestRate>> GetByDepositTypesAsync(IEnumerable<long> depositTypeIds)
        {
            if (!depositTypeIds.Any()) return Enumerable.Empty<DepositInterestRate>();
            
            var sql = @"SELECT * FROM DepositInterestRates 
                        WHERE DepositTypeId IN @depositTypeIds 
                        ORDER BY DepositTypeId, EffectiveFrom DESC, CreatedAt DESC";
            return await Connection.QueryAsync<DepositInterestRate>(sql, new { depositTypeIds });
        }
    }
}
