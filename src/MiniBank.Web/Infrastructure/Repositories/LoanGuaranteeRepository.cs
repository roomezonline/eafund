using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class LoanGuaranteeRepository
    {
        private readonly IDbConnection _connection;

        public LoanGuaranteeRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public Task<IEnumerable<LoanGuarantee>> GetByLoanIdAsync(long loanId)
        {
            const string sql = "SELECT * FROM dbo.LoanGuarantees WHERE LoanId = @loanId ORDER BY GuaranteeId ASC";
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            return _connection.QueryAsync<LoanGuarantee>(sql, new { loanId });
        }

        public async Task<int> ReplaceForLoanAsync(long loanId, IEnumerable<LoanGuarantee> items, IDbTransaction? tx = null)
        {
            await _connection.ExecuteAsync("DELETE FROM dbo.LoanGuarantees WHERE LoanId = @loanId", new { loanId }, tx);

            const string insertSql = @"INSERT INTO dbo.LoanGuarantees (
    LoanId, Type, GuarantorMemberId, GuarantorMemberCode, DepositAccountId, Amount, IsActive, Notes, CreatedAt
)
VALUES (
    @LoanId, @Type, @GuarantorMemberId, @GuarantorMemberCode, @DepositAccountId, @Amount, @IsActive, @Notes, GETUTCDATE()
);";

            var affected = 0;
            foreach (var g in items)
            {
                affected += await _connection.ExecuteAsync(insertSql, new
                {
                    LoanId = loanId,
                    Type = (byte)g.Type,
                    g.GuarantorMemberId,
                    g.GuarantorMemberCode,
                    g.DepositAccountId,
                    g.Amount,
                    IsActive = g.IsActive,
                    g.Notes
                }, tx);
            }

            return affected;
        }
    }
}
