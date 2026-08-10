using Dapper;
using System.Data;
using MiniBank.Web.Models;
using MiniBank.Web.Infrastructure;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class HouseholdReceiptRepository
    {
        private readonly IDbConnection _connection;
        private readonly IDbConnectionFactory _connectionFactory;
        
        public HouseholdReceiptRepository(IDbConnection connection, IDbConnectionFactory connectionFactory) 
        { 
            _connection = connection;
            _connectionFactory = connectionFactory;
        }

        public async Task<long> CreateAsync(HouseholdReceipt model)
        {
            const string sql = @"
INSERT INTO dbo.HouseholdReceipts (
    TargetHouseholdId, DocumentType, TransactionType, CounterpartyName,
    AmountTotal, PayChannel, OccurredAt, CreatedAt, CreatedBy, Status, Notes, ExternalRef
)
VALUES (
    @TargetHouseholdId, @DocumentType, @TransactionType, @CounterpartyName,
    @AmountTotal, @PayChannel, @OccurredAt, GETUTCDATE(), @CreatedBy, @Status, @Notes, @ExternalRef
);

SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
            return await _connection.QuerySingleAsync<long>(sql, model);
        }

        public async Task<HouseholdReceipt?> GetByIdAsync(long id)
        {
            const string sql = "SELECT * FROM dbo.HouseholdReceipts WHERE HouseholdReceiptId=@id";
            return await _connection.QueryFirstOrDefaultAsync<HouseholdReceipt>(sql, new { id });
        }

        public async Task<IEnumerable<PendingReceiptSummary>> GetPendingSummariesAsync(int limit = 10)
        {
            const string sql = @"
SELECT TOP (@limit)
    r.HouseholdReceiptId,
    r.TargetHouseholdId,
    r.AmountTotal,
    ISNULL(SUM(a.Amount), 0) AS AllocatedAmount,
    r.CreatedAt,
    r.Notes
FROM dbo.HouseholdReceipts r
LEFT JOIN dbo.HouseholdReceiptAllocations a ON a.HouseholdReceiptId = r.HouseholdReceiptId
WHERE r.Status = 0 -- pending
GROUP BY r.HouseholdReceiptId, r.TargetHouseholdId, r.AmountTotal, r.CreatedAt, r.Notes
HAVING ISNULL(SUM(a.Amount), 0) < r.AmountTotal
ORDER BY r.CreatedAt DESC;";
            return await _connection.QueryAsync<PendingReceiptSummary>(sql, new { limit });
        }

        public async Task<IEnumerable<ProcessedReceiptSummary>> GetProcessedReceiptsAsync(
            int page = 1, 
            int pageSize = 20, 
            string? keyword = null, 
            string? sortBy = "date", 
            bool sortDesc = true,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var offset = (page - 1) * pageSize;
            var orderBy = sortBy?.ToLower() switch
            {
                "amount" => sortDesc ? "r.AmountTotal DESC" : "r.AmountTotal ASC",
                "household" => sortDesc ? "h.Name DESC" : "h.Name ASC",
                _ => sortDesc ? "r.CreatedAt DESC" : "r.CreatedAt ASC"
            };

            var whereConditions = new List<string>
            {
                "r.Status = 1", // approved receipts
                "EXISTS (SELECT 1 FROM dbo.HouseholdReceiptAllocations a WHERE a.HouseholdReceiptId = r.HouseholdReceiptId)" // has allocations
            };

            var parameters = new DynamicParameters();
            parameters.Add("@offset", offset);
            parameters.Add("@pageSize", pageSize);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                // Search by: household name, member full name (any allocation's member), membership code (MemberId or HouseholdCode), notes, or amount
                whereConditions.Add(@"(
                    h.Name LIKE @keyword
                    OR r.Notes LIKE @keyword
                    OR CAST(r.AmountTotal AS NVARCHAR) LIKE @keyword
                    OR EXISTS (
                        SELECT 1
                        FROM dbo.HouseholdReceiptAllocations a
                        INNER JOIN dbo.Members m ON m.MemberId = a.MemberId
                        WHERE a.HouseholdReceiptId = r.HouseholdReceiptId
                          AND (
                               (m.FirstName + ' ' + m.LastName) LIKE @keyword
                            OR m.FirstName LIKE @keyword
                            OR m.LastName LIKE @keyword
                            OR CAST(m.MemberId AS NVARCHAR) LIKE @keyword
                            OR m.HouseholdCode LIKE @keyword
                          )
                    )
                )");
                parameters.Add("@keyword", $"%{keyword}%");
            }

            if (fromDate.HasValue)
            {
                whereConditions.Add("r.CreatedAt >= @fromDate");
                parameters.Add("@fromDate", fromDate.Value);
            }

            if (toDate.HasValue)
            {
                whereConditions.Add("r.CreatedAt < DATEADD(DAY, 1, @toDate)");
                parameters.Add("@toDate", toDate.Value.AddDays(1)); // Include full day
            }

            var whereClause = string.Join(" AND ", whereConditions);

            var sql = $@"
SELECT 
    r.HouseholdReceiptId,
    r.TargetHouseholdId,
    r.AmountTotal,
    r.CreatedAt,
    r.Notes
FROM dbo.HouseholdReceipts r
INNER JOIN dbo.Households h ON h.HouseholdId = r.TargetHouseholdId
{(whereConditions.Count > 0 ? $"WHERE {whereClause}" : "")}
ORDER BY {orderBy}
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";

            using var connection = await _connectionFactory.CreateConnectionAsync();
            var receipts = (await connection.QueryAsync<ProcessedReceiptSummary>(sql, parameters)).ToList();

            // Load allocations for each receipt
            foreach (var receipt in receipts)
            {
                receipt.Allocations = (await GetAllocationsByReceiptIdAsync(receipt.HouseholdReceiptId)).ToList();
            }

            return receipts;
        }

        private async Task<IEnumerable<ProcessedReceiptAllocation>> GetAllocationsByReceiptIdAsync(long receiptId)
        {
            const string sql = @"
SELECT 
    MemberId,
    PurposeType,
    Amount,
    DepositTypeId
FROM dbo.HouseholdReceiptAllocations 
WHERE HouseholdReceiptId = @receiptId
ORDER BY SortOrder;";
            
            using var connection = await _connectionFactory.CreateConnectionAsync();
            return await connection.QueryAsync<ProcessedReceiptAllocation>(sql, new { receiptId });
        }

        public async Task<ReceiptDeletionInfo> GetDeletionInfoAsync(long id)
        {
            const string sql = @"
                SELECT 
                    (SELECT COUNT(*) FROM dbo.Transactions WHERE ParentReceiptId = @id) AS TransactionsCount,
                    (SELECT COUNT(*) 
                     FROM dbo.Journals j 
                     INNER JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId 
                     WHERE t.ParentReceiptId = @id) AS JournalsCount,
                    (SELECT COUNT(*) 
                     FROM dbo.JournalLines jl
                     INNER JOIN dbo.Journals j ON j.JournalId = jl.JournalId
                     INNER JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId 
                     WHERE t.ParentReceiptId = @id) AS JournalLinesCount,
                    (SELECT COUNT(*) FROM dbo.HouseholdReceiptAllocations WHERE HouseholdReceiptId = @id) AS AllocationsCount";
            
            using var connection = await _connectionFactory.CreateConnectionAsync();
            return await connection.QuerySingleAsync<ReceiptDeletionInfo>(sql, new { id });
        }

        public async Task<bool> DeleteAsync(long id)
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            try
            {
                // Disable triggers temporarily
                await connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.JournalLines");
                await connection.ExecuteAsync("DISABLE TRIGGER ALL ON dbo.Journals");

                using var tx = connection.BeginTransaction();
                try
                {
                    const string sqlJournalIds = @"
SELECT DISTINCT j.JournalId
FROM dbo.Journals j
LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
WHERE (t.ParentReceiptId = @id)
   OR EXISTS (
        SELECT 1
        FROM dbo.HouseholdReceipts hr
        WHERE hr.HouseholdReceiptId = @id
          AND hr.ExternalRef = CONCAT('J:', j.JournalId)
   )";

                    var journalIds = (await connection.QueryAsync<long>(sqlJournalIds, new { id }, tx)).Distinct().ToList();
                    foreach (var jid in journalIds)
                    {
                        if (jid > 0)
                            await UndoLoanPaymentsForManualJournalAsync(connection, jid, tx);
                    }

                    const string sqlJournalIdsTable = @"
DECLARE @JournalIds TABLE (JournalId BIGINT PRIMARY KEY);

INSERT INTO @JournalIds (JournalId)
SELECT DISTINCT j.JournalId
FROM dbo.Journals j
LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
WHERE (t.ParentReceiptId = @id)
   OR EXISTS (
        SELECT 1
        FROM dbo.HouseholdReceipts hr
        WHERE hr.HouseholdReceiptId = @id
          AND hr.ExternalRef = CONCAT('J:', j.JournalId)
   );
";

                    // Delete in proper order to avoid FK constraint violations

                    // 1. Delete journal lines first
                    const string deleteJournalLinesSql = @"
                        " + sqlJournalIdsTable + @"
                        DELETE jl
                        FROM dbo.JournalLines jl
                        INNER JOIN @JournalIds x ON x.JournalId = jl.JournalId;";
                    await connection.ExecuteAsync(deleteJournalLinesSql, new { id }, tx);

                    // 2. Delete journals
                    const string deleteJournalsSql = @"
                        " + sqlJournalIdsTable + @"
                        DELETE j
                        FROM dbo.Journals j
                        INNER JOIN @JournalIds x ON x.JournalId = j.JournalId;";
                    await connection.ExecuteAsync(deleteJournalsSql, new { id }, tx);

                    // 3. Delete transaction audits if they exist
                    const string deleteTransactionAuditsSql = @"
                        DELETE ta FROM dbo.TransactionAudits ta
                        INNER JOIN dbo.Transactions t ON t.TransactionId = ta.TransactionId
                        WHERE t.ParentReceiptId = @id";
                    await connection.ExecuteAsync(deleteTransactionAuditsSql, new { id }, tx);

                    // 4. Delete transactions
                    const string deleteTransactionsSql = @"
                        DELETE FROM dbo.Transactions WHERE ParentReceiptId = @id";
                    await connection.ExecuteAsync(deleteTransactionsSql, new { id }, tx);

                    // 5. Delete allocations
                    const string deleteAllocationsSql = @"
                        DELETE FROM dbo.HouseholdReceiptAllocations WHERE HouseholdReceiptId = @id";
                    await connection.ExecuteAsync(deleteAllocationsSql, new { id }, tx);

                    // 6. Finally delete the receipt
                    const string deleteReceiptSql = @"
                        DELETE FROM dbo.HouseholdReceipts WHERE HouseholdReceiptId = @id";
                    var affected = await connection.ExecuteAsync(deleteReceiptSql, new { id }, tx);

                    tx.Commit();
                    return affected > 0;
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }
                finally
                {
                    // Re-enable triggers
                    await connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.JournalLines");
                    await connection.ExecuteAsync("ENABLE TRIGGER ALL ON dbo.Journals");
                }
            }
            catch
            {
                throw;
            }
        }

        private static async Task UndoLoanPaymentsForManualJournalAsync(IDbConnection connection, long journalId, IDbTransaction tx)
        {
            const string sqlAffectedLoansByJournal = @"
SELECT DISTINCT x.LoanId
FROM (
    SELECT DISTINCT jl.MemberId
    FROM dbo.JournalLines jl
    INNER JOIN dbo.Accounts a ON a.AccountId = COALESCE(jl.ParentAccountId, jl.AccountId)
    WHERE jl.JournalId = @journalId
      AND jl.MemberId IS NOT NULL
      AND a.Code IN ('1301','1302','4101','4102')
) m
CROSS APPLY (
    SELECT TOP 1 l.LoanId
    FROM dbo.Loans l
    WHERE l.MemberId = m.MemberId
      AND l.Status IN (2,3,4,5)
    ORDER BY l.LoanId DESC
) x;";

            var affectedLoanIds = (await connection.QueryAsync<long>(sqlAffectedLoansByJournal, new { journalId }, tx)).Distinct().ToList();

            const string sqlPayments = @"
SELECT PaymentId, LoanId
FROM dbo.LoanPayments
WHERE Notes LIKE @notes";

            var notes = $"ManualJournal:{journalId}%";
            var payments = (await connection.QueryAsync<(long PaymentId, long LoanId)>(sqlPayments, new { notes }, tx)).ToList();

            if (payments.Count > 0)
            {
                var paymentIds = payments.Select(x => x.PaymentId).Distinct().ToList();
                await connection.ExecuteAsync("DELETE FROM dbo.LoanPaymentAllocations WHERE PaymentId IN @ids", new { ids = paymentIds }, tx);
                await connection.ExecuteAsync("DELETE FROM dbo.LoanPayments WHERE PaymentId IN @ids", new { ids = paymentIds }, tx);
            }

            var loanIds = payments.Select(x => x.LoanId).Concat(affectedLoanIds).Distinct().ToList();
            if (loanIds.Count == 0)
                return;

            const string sqlDeleteOrphanManualJournalPayments = @"
DECLARE @OrphanPayments TABLE (PaymentId BIGINT PRIMARY KEY);

INSERT INTO @OrphanPayments (PaymentId)
SELECT p.PaymentId
FROM dbo.LoanPayments p
OUTER APPLY (
  SELECT TRY_CONVERT(BIGINT,
    CASE
      WHEN p.Notes LIKE 'ManualJournal:%' THEN
        SUBSTRING(
          SUBSTRING(p.Notes, LEN('ManualJournal:') + 1, 50),
          1,
          PATINDEX('%[^0-9]%', SUBSTRING(p.Notes, LEN('ManualJournal:') + 1, 50) + 'X') - 1
        )
      ELSE NULL
    END
  ) AS JournalId
) j
WHERE p.LoanId = @loanId
  AND p.Notes LIKE 'ManualJournal:%'
  AND (j.JournalId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Journals jj WHERE jj.JournalId = j.JournalId));

DELETE FROM dbo.LoanPaymentAllocations WHERE PaymentId IN (SELECT PaymentId FROM @OrphanPayments);
DELETE FROM dbo.LoanPayments WHERE PaymentId IN (SELECT PaymentId FROM @OrphanPayments);";

            const string sqlRecompute = @"
UPDATE li
SET
  PaidPrincipal = ISNULL(x.PaidPrincipal, 0),
  PaidInterest  = ISNULL(x.PaidInterest, 0),
  PaidPenalty   = ISNULL(x.PaidPenalty, 0),
  Status = CASE
    WHEN (ISNULL(x.PaidPrincipal,0) + ISNULL(x.PaidInterest,0)) >= (li.PrincipalAmount + li.InterestAmount) - 0.01 THEN 3
    WHEN (ISNULL(x.PaidPrincipal,0) + ISNULL(x.PaidInterest,0) + ISNULL(x.PaidPenalty,0)) > 0 THEN 2
    ELSE 1
  END
FROM dbo.LoanInstallments li
OUTER APPLY (
  SELECT
    SUM(CASE WHEN a.Type = 3 THEN a.Amount ELSE 0 END) AS PaidPrincipal,
    SUM(CASE WHEN a.Type = 2 THEN a.Amount ELSE 0 END) AS PaidInterest,
    SUM(CASE WHEN a.Type = 1 THEN a.Amount ELSE 0 END) AS PaidPenalty
  FROM dbo.LoanPaymentAllocations a
  JOIN dbo.LoanPayments p ON p.PaymentId = a.PaymentId
  WHERE p.LoanId = li.LoanId
    AND a.InstallmentId = li.InstallmentId
) x
WHERE li.LoanId = @loanId";

            const string sqlUpdateLoanStatus = @"
UPDATE l
SET
  Status = CASE
    WHEN r.RemainingCount <= 0 THEN 5
    WHEN p.HasPayments = 1 THEN 3
    WHEN d.HasDisbursement = 1 THEN 3
    ELSE 2
  END,
  UpdatedAt = GETUTCDATE()
FROM dbo.Loans l
CROSS APPLY (SELECT COUNT(1) AS RemainingCount FROM dbo.LoanInstallments WHERE LoanId = l.LoanId AND Status <> 3) r
CROSS APPLY (SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.LoanPayments WHERE LoanId = l.LoanId) THEN 1 ELSE 0 END AS HasPayments) p
CROSS APPLY (
  SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.vw_ApprovedJournals j
    WHERE j.Notes LIKE '%LoanDisburse%'
      AND j.Notes LIKE CONCAT('%Loan:', l.LoanId, '%')
  ) THEN 1 ELSE 0 END AS HasDisbursement
) d
WHERE l.LoanId = @loanId";

            foreach (var loanId in loanIds)
            {
                await connection.ExecuteAsync(sqlDeleteOrphanManualJournalPayments, new { loanId }, tx);
                await connection.ExecuteAsync(sqlRecompute, new { loanId }, tx);
                await connection.ExecuteAsync(sqlUpdateLoanStatus, new { loanId }, tx);
            }
        }
    }
}
