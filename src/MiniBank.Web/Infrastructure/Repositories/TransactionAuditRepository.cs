using Dapper;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class TransactionAuditRepository
    {
        private readonly IDbConnection _db;
        public TransactionAuditRepository(IDbConnection db) { _db = db; }

        public async Task<long> LogAsync(long transactionId, string stage, string? message = null, string? createdBy = null)
        {
            const string sql = @"
INSERT INTO dbo.TransactionAudits (TransactionId, Stage, Message, CreatedAt, CreatedBy)
OUTPUT INSERTED.AuditId
VALUES (@transactionId, @stage, @message, SYSUTCDATETIME(), @createdBy);";
            return await _db.ExecuteScalarAsync<long>(sql, new { transactionId, stage, message, createdBy });
        }

        public async Task<IEnumerable<(DateTime CreatedAt, string Stage, string? Message)>> GetByTransactionAsync(long transactionId)
        {
            const string sql = @"SELECT CreatedAt, Stage, Message FROM dbo.TransactionAudits WHERE TransactionId=@transactionId ORDER BY CreatedAt ASC";
            return await _db.QueryAsync<(DateTime, string, string?)>(sql, new { transactionId });
        }

        public async Task<IEnumerable<dynamic>> GetRecentAsync(int top = 50)
        {
            const string sql = @"
SELECT TOP(@top) t.TransactionId, t.TypeId, t.MemberId, t.MemberDepositAccountId, t.AmountTotal, t.OccurredAt, t.CreatedAt, t.Status,
       (SELECT TOP 1 Stage FROM dbo.TransactionAudits a WHERE a.TransactionId = t.TransactionId ORDER BY CreatedAt DESC) AS LastStage,
       (SELECT COUNT(1) FROM dbo.TransactionAudits a WHERE a.TransactionId = t.TransactionId) AS AuditCount
FROM dbo.Transactions t
ORDER BY t.TransactionId DESC";
            return await _db.QueryAsync(sql, new { top });
        }
    }
}
