using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class LoanInstallmentRepository
    {
        private readonly IDbConnection _connection;

        public LoanInstallmentRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public Task<IEnumerable<LoanInstallment>> GetByLoanIdAsync(long loanId)
        {
            const string sql = "SELECT * FROM dbo.LoanInstallments WHERE LoanId = @loanId ORDER BY InstallmentNo ASC";
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            return _connection.QueryAsync<LoanInstallment>(sql, new { loanId });
        }

        public Task<IEnumerable<LoanInstallment>> GetByLoanIdAsync(long loanId, IDbTransaction? tx)
        {
            const string sql = "SELECT * FROM dbo.LoanInstallments WHERE LoanId = @loanId ORDER BY InstallmentNo ASC";
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            return _connection.QueryAsync<LoanInstallment>(sql, new { loanId }, tx);
        }

        public sealed class LoanInterestAggRow
        {
            public long LoanId { get; set; }
            public decimal SumInterest { get; set; }
        }

        public sealed class LoanRemainingAggRow
        {
            public long LoanId { get; set; }
            public decimal RemainingPrincipal { get; set; }
            public decimal RemainingInterest { get; set; }
        }

        public async Task<IReadOnlyDictionary<long, decimal>> GetInterestSumsByLoanIdsAsync(IEnumerable<long> loanIds, IDbTransaction? tx = null)
        {
            var ids = (loanIds ?? Array.Empty<long>()).Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return new Dictionary<long, decimal>();

            const string sql = @"SELECT LoanId, SUM(InterestAmount) AS SumInterest
FROM dbo.LoanInstallments
WHERE LoanId IN @ids
GROUP BY LoanId";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var rows = (await _connection.QueryAsync<LoanInterestAggRow>(sql, new { ids }, tx)).ToList();
            return rows.ToDictionary(x => x.LoanId, x => x.SumInterest);
        }

        public async Task<IReadOnlyDictionary<long, LoanRemainingAggRow>> GetRemainingSumsByLoanIdsAsync(IEnumerable<long> loanIds, IDbTransaction? tx = null)
        {
            var ids = (loanIds ?? Array.Empty<long>()).Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return new Dictionary<long, LoanRemainingAggRow>();

            const string sql = @"SELECT
    LoanId,
    SUM(CASE WHEN PrincipalAmount - PaidPrincipal > 0 THEN (PrincipalAmount - PaidPrincipal) ELSE 0 END) AS RemainingPrincipal,
    SUM(CASE WHEN InterestAmount - PaidInterest > 0 THEN (InterestAmount - PaidInterest) ELSE 0 END) AS RemainingInterest
FROM dbo.LoanInstallments
WHERE LoanId IN @ids
GROUP BY LoanId";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var rows = (await _connection.QueryAsync<LoanRemainingAggRow>(sql, new { ids }, tx)).ToList();
            return rows.ToDictionary(x => x.LoanId, x => x);
        }

        public async Task<int> BulkInsertAsync(IEnumerable<LoanInstallment> installments, IDbTransaction? tx = null)
        {
            const string sql = @"INSERT INTO dbo.LoanInstallments (
    LoanId, InstallmentNo, DueDate,
    OpeningBalance, EmiAmount, InterestAmount, PrincipalAmount, ClosingBalance,
    PaidPrincipal, PaidInterest, PaidPenalty,
    Status, CreatedAt
)
VALUES (
    @LoanId, @InstallmentNo, @DueDate,
    @OpeningBalance, @EmiAmount, @InterestAmount, @PrincipalAmount, @ClosingBalance,
    0, 0, 0,
    @Status, GETUTCDATE()
);";

            var affected = 0;
            foreach (var it in installments)
            {
                affected += await _connection.ExecuteAsync(sql, new
                {
                    it.LoanId,
                    it.InstallmentNo,
                    DueDate = it.DueDate.Date,
                    it.OpeningBalance,
                    it.EmiAmount,
                    it.InterestAmount,
                    it.PrincipalAmount,
                    it.ClosingBalance,
                    Status = (byte)it.Status
                }, tx);
            }

            return affected;
        }

        public Task DeleteByLoanIdAsync(long loanId, IDbTransaction? tx = null)
        {
            const string sql = "DELETE FROM dbo.LoanInstallments WHERE LoanId = @loanId";
            return _connection.ExecuteAsync(sql, new { loanId }, tx);
        }
    }
}
