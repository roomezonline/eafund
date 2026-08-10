using Dapper;
using System.Data;
using MiniBank.Web.Models;
using MiniBank.Web.Infrastructure;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class AccountingReportRepository
    {
        private readonly IDbConnection _db;
        private readonly IDbConnectionFactory _connectionFactory;
        
        public AccountingReportRepository(IDbConnection db, IDbConnectionFactory connectionFactory) 
        { 
            _db = db; 
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<TrialBalanceRow>> GetTrialBalanceAsync()
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            
            // Check if vw_TrialBalance exists, fallback to direct query if not
            try
            {
                const string sql = @"SELECT AccountId, AccountCode, AccountName, AccountType, TotalDebit, TotalCredit, Net FROM dbo.vw_TrialBalance ORDER BY AccountCode";
                return await connection.QueryAsync<TrialBalanceRow>(sql);
            }
            catch
            {
                // Fallback query if view doesn't exist yet
                const string fallbackSql = @"
                    SELECT a.AccountId, a.Code AS AccountCode, a.Name AS AccountName, a.AccountType,
                           ISNULL(SUM(jl.Debit), 0) AS TotalDebit, 
                           ISNULL(SUM(jl.Credit), 0) AS TotalCredit, 
                           ISNULL(SUM(jl.Debit - jl.Credit), 0) AS Net
                    FROM dbo.Accounts a
                    LEFT JOIN dbo.JournalLines jl ON jl.AccountId = a.AccountId
                    LEFT JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
                    WHERE a.IsActive = 1
                    GROUP BY a.AccountId, a.Code, a.Name, a.AccountType
                    ORDER BY a.Code";
                return await connection.QueryAsync<TrialBalanceRow>(fallbackSql);
            }
        }

        public async Task<BalanceSheetSnapshot> GetBalanceSheetAsOfAsync(DateTime asOf)
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            var p = new DynamicParameters();
            p.Add("@AsOf", asOf, DbType.DateTime2);
            using var multi = await connection.QueryMultipleAsync("dbo.sp_BalanceSheet_AsOf", p, commandType: CommandType.StoredProcedure);
            var row = await multi.ReadFirstOrDefaultAsync();
            if (row == null) return new BalanceSheetSnapshot();
            return new BalanceSheetSnapshot
            {
                Assets = (decimal)row.Assets,
                Liabilities = (decimal)row.Liabilities,
                Equity = (decimal)row.Equity,
                Income = (decimal)row.Income,
                Expense = (decimal)row.Expense
            };
        }

        public async Task<decimal> GetMemberDepositBalanceAsync(long memberId)
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            const string sql = @"SELECT ISNULL(DepositLiability, 0) FROM dbo.vw_MemberDepositBalances WHERE MemberId = @memberId";
            var val = await connection.ExecuteScalarAsync<decimal?>(sql, new { memberId });
            return val ?? 0m;
        }


    }
}
