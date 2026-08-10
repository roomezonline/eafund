using Dapper;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class LoanPaymentRepository
    {
        private readonly IDbConnection _connection;

        public LoanPaymentRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public Task<bool> HasAnyForLoanAsync(long loanId, IDbTransaction? tx = null)
        {
            const string sql = @"SELECT CASE WHEN EXISTS(
    SELECT 1 FROM dbo.LoanPayments WHERE LoanId = @loanId
) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END;";

            return _connection.QuerySingleAsync<bool>(sql, new { loanId }, tx);
        }
    }
}
