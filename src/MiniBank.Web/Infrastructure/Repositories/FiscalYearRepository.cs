using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class FiscalYearRepository
    {
        private readonly IDbConnection _connection;

        public FiscalYearRepository(IDbConnection connection) { _connection = connection; }

        public async Task<IEnumerable<FiscalYear>> GetAllAsync()
        {
            return await _connection.QueryAsync<FiscalYear>(
                "SELECT * FROM FiscalYears ORDER BY YearCode DESC");
        }

        public async Task<FiscalYear?> GetByIdAsync(int id)
        {
            return await _connection.QueryFirstOrDefaultAsync<FiscalYear>(
                "SELECT * FROM FiscalYears WHERE FiscalYearId = @id", new { id });
        }

        public async Task<FiscalYear?> GetByYearCodeAsync(int yearCode)
        {
            return await _connection.QueryFirstOrDefaultAsync<FiscalYear>(
                "SELECT * FROM FiscalYears WHERE YearCode = @yearCode", new { yearCode });
        }

        public async Task<FiscalYear?> GetOpenYearForDateAsync(DateTime date, IDbTransaction? tx = null)
        {
            return await _connection.QueryFirstOrDefaultAsync<FiscalYear>(
                "SELECT * FROM FiscalYears WHERE StartDate <= @date AND DATEADD(DAY, 1, EndDate) > @date AND IsOpen = 1",
                new { date }, transaction: tx);
        }

        public async Task<int> CreateAsync(FiscalYear model)
        {
            var sql = @"INSERT INTO FiscalYears (YearCode, Title, StartDate, EndDate, IsOpen, IsClosed, Notes, CreatedAt)
                        OUTPUT INSERTED.FiscalYearId
                        VALUES (@YearCode, @Title, @StartDate, @EndDate, @IsOpen, @IsClosed, @Notes, GETUTCDATE())";
            return await _connection.QuerySingleAsync<int>(sql, model);
        }

        public async Task UpdateAsync(FiscalYear model)
        {
            var sql = @"UPDATE FiscalYears SET
                            Title = @Title,
                            StartDate = @StartDate,
                            EndDate = @EndDate,
                            IsOpen = @IsOpen,
                            IsClosed = @IsClosed,
                            ClosedAt = @ClosedAt,
                            ClosedBy = @ClosedBy,
                            Notes = @Notes
                        WHERE FiscalYearId = @FiscalYearId";
            await _connection.ExecuteAsync(sql, model);
        }

        public async Task DeleteAsync(int id)
        {
            await _connection.ExecuteAsync("DELETE FROM FiscalYears WHERE FiscalYearId = @id", new { id });
        }

        public async Task<IEnumerable<FiscalYearBalance>> GetBalancesAsync(int fiscalYearId)
        {
            return await _connection.QueryAsync<FiscalYearBalance>(
                "SELECT * FROM FiscalYearBalances WHERE FiscalYearId = @fiscalYearId ORDER BY BalanceId",
                new { fiscalYearId });
        }

        public async Task SaveBalancesAsync(int fiscalYearId, IEnumerable<FiscalYearBalance> balances)
        {
            await _connection.ExecuteAsync(
                "DELETE FROM FiscalYearBalances WHERE FiscalYearId = @fiscalYearId",
                new { fiscalYearId });

            foreach (var b in balances)
            {
                await _connection.ExecuteAsync(
                    @"INSERT INTO FiscalYearBalances (FiscalYearId, AccountId, OpeningDebit, OpeningCredit)
                      VALUES (@FiscalYearId, @AccountId, @OpeningDebit, @OpeningCredit)",
                    new { fiscalYearId, b.AccountId, b.OpeningDebit, b.OpeningCredit });
            }
        }
    }
}
