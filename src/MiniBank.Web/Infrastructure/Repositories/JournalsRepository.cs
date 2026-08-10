using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class JournalsRepository
    {
        private readonly IDbConnection _db;
        private readonly FiscalYearRepository _fyRepo;

        private const string SelectColumns = "JournalId, TransactionId, OccurredAt, CreatedAt, CreatedBy, Notes, Status, IsDeleted, SerialNumber, FiscalYearId, DocumentNumber";

        public JournalsRepository(IDbConnection db, FiscalYearRepository fyRepo) { _db = db; _fyRepo = fyRepo; }

        public async Task<long> CreateJournalAsync(long transactionId, DateTime occurredAt, string createdBy, string? notes = null)
        {
            var fy = await _fyRepo.GetOpenYearForDateAsync(occurredAt);
            if (fy == null)
                throw new InvalidOperationException("برای تاریخ " + occurredAt.ToString("yyyy/MM/dd") + " سال مالی بازی وجود ندارد. لطفاً با مدیر سیستم تماس بگیرید.");

            const string sql = @"
                INSERT INTO dbo.Journals (TransactionId, OccurredAt, CreatedAt, CreatedBy, Notes)
                VALUES (@transactionId, @occurredAt, GETUTCDATE(), @createdBy, @notes);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT)";

            var journalId = await _db.QuerySingleAsync<long>(sql, new { transactionId, occurredAt, createdBy, notes });

            await EnsureSerialCounterAsync(fy.FiscalYearId);
            var docNum = await GetNextDocumentNumberAsync(fy.FiscalYearId);
            await AssignDocumentNumberAsync(journalId, docNum);

            return journalId;
        }

        public async Task<long> CreateManualJournalAsync(DateTime occurredAt, string createdBy, string? notes = null, IDbTransaction? tx = null, byte status = 2)
        {
            var fy = await _fyRepo.GetOpenYearForDateAsync(occurredAt, tx);
            if (fy == null)
                throw new InvalidOperationException("برای تاریخ " + occurredAt.ToString("yyyy/MM/dd") + " سال مالی بازی وجود ندارد. لطفاً با مدیر سیستم تماس بگیرید.");

            const string sql = @"
                INSERT INTO dbo.Journals (TransactionId, OccurredAt, CreatedAt, CreatedBy, Notes, Status)
                VALUES (NULL, @occurredAt, GETUTCDATE(), @createdBy, @notes, @status);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT)";

            var journalId = await _db.QuerySingleAsync<long>(sql, new { occurredAt, createdBy, notes, status }, tx);

            if (status == 2)
            {
                await EnsureSerialCounterAsync(fy.FiscalYearId, tx);
                var docNum = await GetNextDocumentNumberAsync(fy.FiscalYearId, tx);
                await AssignDocumentNumberAsync(journalId, docNum, tx);
            }

            return journalId;
        }

        public async Task<Journal?> GetByIdAsync(long journalId)
        {
            const string sql = @"
                SELECT " + SelectColumns + @"
                FROM dbo.Journals
                WHERE JournalId = @journalId";

            return await _db.QuerySingleOrDefaultAsync<Journal>(sql, new { journalId });
        }

        public async Task<Journal?> GetByTransactionIdAsync(long transactionId)
        {
            const string sql = @"
                SELECT " + SelectColumns + @"
                FROM dbo.Journals
                WHERE TransactionId = @transactionId";

            return await _db.QuerySingleOrDefaultAsync<Journal>(sql, new { transactionId });
        }

        public async Task<List<Journal>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int limit = 100)
        {
            const string sql = @"
                SELECT TOP (@limit) " + SelectColumns + @"
                FROM dbo.Journals
                WHERE OccurredAt >= @fromDate AND OccurredAt < DATEADD(DAY, 1, @toDate)
                ORDER BY OccurredAt DESC, JournalId DESC";

            var result = await _db.QueryAsync<Journal>(sql, new { fromDate, toDate, limit });
            return result.ToList();
        }

        public async Task UpdateNotesAsync(long journalId, string notes)
        {
            const string sql = "UPDATE dbo.Journals SET Notes = @notes WHERE JournalId = @journalId";
            await _db.ExecuteAsync(sql, new { journalId, notes });
        }

        public async Task<bool> ExistsAsync(long journalId)
        {
            const string sql = "SELECT COUNT(1) FROM dbo.Journals WHERE JournalId = @journalId";
            var count = await _db.QuerySingleAsync<int>(sql, new { journalId });
            return count > 0;
        }

        public async Task UpdateStatusAsync(long journalId, byte status, IDbTransaction? tx = null)
        {
            const string sql = "UPDATE dbo.Journals SET Status = @status WHERE JournalId = @journalId";
            await _db.ExecuteAsync(sql, new { journalId, status }, tx);
        }

        public async Task SoftDeleteAsync(long journalId, IDbTransaction? tx = null)
        {
            const string sql = "UPDATE dbo.Journals SET IsDeleted = 1 WHERE JournalId = @journalId";
            await _db.ExecuteAsync(sql, new { journalId }, tx);
        }

        public async Task AssignSerialNumberAsync(long journalId, int serialNumber, IDbTransaction? tx = null)
        {
            const string sql = "UPDATE dbo.Journals SET SerialNumber = @serialNumber WHERE JournalId = @journalId";
            await _db.ExecuteAsync(sql, new { journalId, serialNumber }, tx);
        }

        public async Task<int> GetMaxSerialNumberAsync(DateTime fromDate, DateTime toDate)
        {
            const string sql = @"
                SELECT ISNULL(MAX(SerialNumber), 0)
                FROM dbo.Journals
                WHERE OccurredAt >= @fromDate AND OccurredAt < DATEADD(DAY, 1, @toDate)
                  AND SerialNumber IS NOT NULL";
            return await _db.QuerySingleAsync<int>(sql, new { fromDate, toDate });
        }

        public async Task AssignDocumentNumberAsync(long journalId, int documentNumber, IDbTransaction? tx = null)
        {
            const string sql = "UPDATE dbo.Journals SET DocumentNumber = @documentNumber WHERE JournalId = @journalId";
            await _db.ExecuteAsync(sql, new { journalId, documentNumber }, tx);
        }

        public async Task<int> GetNextDocumentNumberAsync(int fiscalYearId, IDbTransaction? tx = null)
        {
            const string sql = @"
                UPDATE dbo.SerialCounters
                SET LastDocumentNumber = LastDocumentNumber + 1
                OUTPUT INSERTED.LastDocumentNumber
                WHERE FiscalYearId = @fiscalYearId";
            return await _db.QuerySingleAsync<int>(sql, new { fiscalYearId }, tx);
        }

        public async Task<int> GetCurrentDocumentNumberAsync(int fiscalYearId)
        {
            const string sql = "SELECT ISNULL(LastDocumentNumber, 0) FROM dbo.SerialCounters WHERE FiscalYearId = @fiscalYearId";
            return await _db.QuerySingleAsync<int>(sql, new { fiscalYearId });
        }

        public async Task<int> GetMaxDocumentNumberAsync(int fiscalYearId, IDbTransaction? tx = null)
        {
            const string sql = @"
                SELECT ISNULL(MAX(DocumentNumber), 0)
                FROM dbo.Journals
                WHERE FiscalYearId = @fiscalYearId AND DocumentNumber IS NOT NULL";
            return await _db.QuerySingleAsync<int>(sql, new { fiscalYearId }, tx);
        }

        public async Task EnsureSerialCounterAsync(int fiscalYearId, IDbTransaction? tx = null)
        {
            var exists = await _db.QuerySingleAsync<int>(
                "SELECT COUNT(1) FROM dbo.SerialCounters WHERE FiscalYearId = @fiscalYearId", new { fiscalYearId }, tx);
            if (exists == 0)
            {
                var maxDoc = await GetMaxDocumentNumberAsync(fiscalYearId, tx);
                await _db.ExecuteAsync(
                    "INSERT INTO dbo.SerialCounters (FiscalYearId, LastDocumentNumber) VALUES (@fiscalYearId, @maxDoc)",
                    new { fiscalYearId, maxDoc }, tx);
            }
        }

        public async Task<List<Journal>> GetByStatusRangeAsync(byte status, DateTime? fromDate, DateTime? toDate)
        {
            var sql = @"
                SELECT " + SelectColumns + @"
                FROM dbo.Journals
                WHERE Status = @status AND IsDeleted = 0";
            if (fromDate.HasValue)
                sql += " AND OccurredAt >= @fromDate";
            if (toDate.HasValue)
                sql += " AND OccurredAt < DATEADD(DAY, 1, @toDate)";
            sql += " ORDER BY OccurredAt ASC, JournalId ASC";

            var result = await _db.QueryAsync<Journal>(sql, new { status, fromDate, toDate });
            return result.ToList();
        }

        public async Task<List<Journal>> GetDeletedAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var sql = @"
                SELECT " + SelectColumns + @"
                FROM dbo.Journals
                WHERE IsDeleted = 1";
            if (fromDate.HasValue)
                sql += " AND OccurredAt >= @fromDate";
            if (toDate.HasValue)
                sql += " AND OccurredAt < DATEADD(DAY, 1, @toDate)";
            sql += " ORDER BY OccurredAt DESC, JournalId DESC";

            var result = await _db.QueryAsync<Journal>(sql, new { fromDate, toDate });
            return result.ToList();
        }
    }
}
