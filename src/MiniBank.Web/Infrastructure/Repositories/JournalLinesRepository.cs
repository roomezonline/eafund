using Dapper;
using System.Data;
using MiniBank.Web.Models;
using System.Data.Common;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class JournalLinesRepository
    {
        private readonly IDbConnection _db;
        public JournalLinesRepository(IDbConnection db) { _db = db; }

        private bool? _hasParentAccountId;

        public async Task<long> AddLineAsync(long journalId, int lineNo, long accountId, long? memberId, decimal debit, decimal credit, string? description = null, long? loanId = null)
        {
            const string sql = @"
                INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, MemberId, Debit, Credit, Description, LoanId)
                VALUES (@journalId, @lineNo, @accountId, @memberId, @debit, @credit, @description, @loanId);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT)";
            
            return await _db.QuerySingleAsync<long>(sql, new { journalId, lineNo, accountId, memberId, debit, credit, description, loanId });
        }

        public async Task<long> AddLineAsync(long journalId, int lineNo, long accountId, long? parentAccountId, long? memberId, decimal debit, decimal credit, string? description = null, IDbTransaction? tx = null, long? loanId = null)
        {
            await EnsureOpenAsync();
            var hasParent = await HasParentAccountIdAsync(tx);
            if (!hasParent)
                return await _db.QuerySingleAsync<long>(@"
                    INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, MemberId, Debit, Credit, Description, LoanId)
                    VALUES (@journalId, @lineNo, @accountId, @memberId, @debit, @credit, @description, @loanId);
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    new { journalId, lineNo, accountId, memberId, debit, credit, description, loanId }, tx);

            return await _db.QuerySingleAsync<long>(@"
                INSERT INTO dbo.JournalLines (JournalId, [LineNo], AccountId, ParentAccountId, MemberId, Debit, Credit, Description, LoanId)
                VALUES (@journalId, @lineNo, @accountId, @parentAccountId, @memberId, @debit, @credit, @description, @loanId);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                new { journalId, lineNo, accountId, parentAccountId, memberId, debit, credit, description, loanId }, tx);
        }

        public async Task<List<JournalLine>> GetByJournalIdAsync(long journalId)
        {
            const string sql = @"
                SELECT jl.JournalLineId, jl.JournalId, jl.[LineNo] AS [LineNo], jl.AccountId, jl.MemberId, 
                       jl.Debit, jl.Credit, jl.Description, jl.LoanId
                FROM dbo.JournalLines jl
                WHERE jl.JournalId = @journalId
                ORDER BY jl.[LineNo]";
            
            var result = await _db.QueryAsync<JournalLine>(sql, new { journalId });
            return result.ToList();
        }

        private async Task<bool> HasParentAccountIdAsync(IDbTransaction? tx)
        {
            if (_hasParentAccountId.HasValue)
                return _hasParentAccountId.Value;

            const string sql = "SELECT CASE WHEN COL_LENGTH('dbo.JournalLines','ParentAccountId') IS NULL THEN 0 ELSE 1 END";
            var has = await _db.QuerySingleAsync<int>(sql, transaction: tx);
            _hasParentAccountId = has == 1;
            return _hasParentAccountId.Value;
        }

        private async Task EnsureOpenAsync()
        {
            if (_db.State != ConnectionState.Open)
            {
                if (_db is DbConnection dbc)
                    await dbc.OpenAsync();
                else
                    _db.Open();
            }
        }

        public async Task<List<JournalLine>> GetByAccountIdAsync(long accountId, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
        {
            var whereClause = "WHERE jl.AccountId = @accountId";
            if (fromDate.HasValue) whereClause += " AND j.OccurredAt >= @fromDate";
            if (toDate.HasValue) whereClause += " AND j.OccurredAt < DATEADD(DAY, 1, @toDate)";

            var sql = $@"
                SELECT TOP (@limit) jl.JournalLineId, jl.JournalId, jl.[LineNo] AS [LineNo], jl.AccountId, jl.MemberId, 
                       jl.Debit, jl.Credit, jl.Description, jl.LoanId
                FROM dbo.JournalLines jl
                JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
                {whereClause}
                ORDER BY j.OccurredAt DESC, jl.[LineNo]";

            var result = await _db.QueryAsync<JournalLine>(sql, new { accountId, fromDate, toDate, limit });
            return result.ToList();
        }

        public async Task<List<JournalLine>> GetByMemberIdAsync(long memberId, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
        {
            var whereClause = "WHERE jl.MemberId = @memberId";
            if (fromDate.HasValue) whereClause += " AND j.OccurredAt >= @fromDate";
            if (toDate.HasValue) whereClause += " AND j.OccurredAt < DATEADD(DAY, 1, @toDate)";

            var sql = $@"
                SELECT TOP (@limit) jl.JournalLineId, jl.JournalId, jl.[LineNo], jl.AccountId, jl.MemberId, 
                       jl.Debit, jl.Credit, jl.Description, jl.LoanId
                FROM dbo.JournalLines jl
                JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
                {whereClause}
                ORDER BY j.OccurredAt DESC, jl.[LineNo]";
            
            var result = await _db.QueryAsync<JournalLine>(sql, new { memberId, fromDate, toDate, limit });
            return result.ToList();
        }

        public async Task<(decimal TotalDebit, decimal TotalCredit)> GetJournalBalanceAsync(long journalId)
        {
            const string sql = @"
                SELECT SUM(Debit) AS TotalDebit, SUM(Credit) AS TotalCredit
                FROM dbo.JournalLines 
                WHERE JournalId = @journalId";
            
            var result = await _db.QuerySingleAsync<(decimal, decimal)>(sql, new { journalId });
            return result;
        }

        public async Task<bool> ValidateBalanceAsync(long journalId)
        {
            var (totalDebit, totalCredit) = await GetJournalBalanceAsync(journalId);
            return Math.Abs(totalDebit - totalCredit) < 0.01m; // Allow for minor rounding differences
        }

        public async Task DeleteByJournalIdAsync(long journalId)
        {
            const string sql = "DELETE FROM dbo.JournalLines WHERE JournalId = @journalId";
            await _db.ExecuteAsync(sql, new { journalId });
        }

        public async Task<int> GetNextLineNoAsync(long journalId)
        {
            const string sql = @"
                SELECT ISNULL(MAX([LineNo]), 0) + 1 
                FROM dbo.JournalLines 
                WHERE JournalId = @journalId";
            
            return await _db.QuerySingleAsync<int>(sql, new { journalId });
        }
    }
}
