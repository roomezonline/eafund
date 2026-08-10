using Dapper;
using System.Data;
using MiniBank.Web.Models.Reports;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class TransactionReportRepository
    {
        private readonly IDbConnection _connection;
        public TransactionReportRepository(IDbConnection connection) { _connection = connection; }

        public sealed class MemberEffectiveDepositAggRow
        {
            public long MemberId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public string? MemberCode { get; set; }
            public string? HouseholdCode { get; set; }
            public long? HouseholdOwnerMemberId { get; set; }
            public string NationalCode { get; set; } = string.Empty;
            public bool IsActive { get; set; }

            public decimal PrincipalCredit { get; set; }
            public decimal PrincipalDebit { get; set; }
            public decimal InterestCredit { get; set; }
            public decimal InterestDebit { get; set; }
        }

        public async Task<IReadOnlyList<MemberEffectiveDepositAggRow>> GetMemberEffectiveDepositAggForPeriodAsync(
            DateTime creditsToExclusive,
            DateTime debitsToExclusive,
            bool activeMembersOnly = true)
        {
            const string rootSql = @"
SELECT
  MAX(CASE WHEN Code = '2201' THEN AccountId END) AS PrincipalRootId,
  MAX(CASE WHEN Code = '2202' THEN AccountId END) AS InterestRootId
FROM dbo.Accounts
WHERE IsActive = 1 AND Code IN ('2201','2202');";

            var roots = await _connection.QuerySingleOrDefaultAsync<(long? PrincipalRootId, long? InterestRootId)>(rootSql);
            var principalRootId = roots.PrincipalRootId ?? 0;
            var interestRootId = roots.InterestRootId ?? 0;
            if (principalRootId <= 0 || interestRootId <= 0)
                return Array.Empty<MemberEffectiveDepositAggRow>();

            var sql = @"
;WITH principal_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @principalRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN principal_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
interest_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @interestRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN interest_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
subs AS (
    SELECT a.MemberId, MAX(a.AccountId) AS AccountId, MAX(a.Code) AS Code
    FROM dbo.Accounts a
    WHERE a.IsActive = 1
      AND a.MemberId IS NOT NULL
      AND a.Code LIKE 'M-%'
    GROUP BY a.MemberId
)
SELECT
  m.MemberId,
  (m.FirstName + ' ' + m.LastName) AS MemberName,
  m.NationalCode,
  m.IsActive,
  m.HouseholdCode,
  h.OwnerMemberId AS HouseholdOwnerMemberId,
  s.Code AS MemberCode,
  SUM(CASE WHEN j.JournalId IS NOT NULL AND pt.AccountId IS NOT NULL AND COALESCE(t.OccurredAt, j.OccurredAt) < @creditsTo THEN ISNULL(jl.Credit,0) ELSE 0 END) AS PrincipalCredit,
  SUM(CASE WHEN j.JournalId IS NOT NULL AND pt.AccountId IS NOT NULL AND COALESCE(t.OccurredAt, j.OccurredAt) < @debitsTo  THEN ISNULL(jl.Debit,0)  ELSE 0 END) AS PrincipalDebit,
  SUM(CASE WHEN j.JournalId IS NOT NULL AND it.AccountId IS NOT NULL AND COALESCE(t.OccurredAt, j.OccurredAt) < @creditsTo THEN ISNULL(jl.Credit,0) ELSE 0 END) AS InterestCredit,
  SUM(CASE WHEN j.JournalId IS NOT NULL AND it.AccountId IS NOT NULL AND COALESCE(t.OccurredAt, j.OccurredAt) < @debitsTo  THEN ISNULL(jl.Debit,0)  ELSE 0 END) AS InterestDebit
FROM dbo.Members m
LEFT JOIN dbo.Households h ON h.HouseholdCode = m.HouseholdCode AND h.IsActive = 1
LEFT JOIN subs s ON s.MemberId = m.MemberId
LEFT JOIN dbo.JournalLines jl ON jl.AccountId = s.AccountId
LEFT JOIN principal_tree pt ON jl.ParentAccountId = pt.AccountId
LEFT JOIN interest_tree it ON jl.ParentAccountId = it.AccountId
LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
LEFT JOIN dbo.Transactions t ON j.TransactionId = t.TransactionId
WHERE (@activeOnly = 0 OR m.IsActive = 1)
GROUP BY m.MemberId, m.FirstName, m.LastName, m.NationalCode, m.IsActive, m.HouseholdCode, h.OwnerMemberId, s.Code
ORDER BY m.MemberId ASC;";

            var rows = (await _connection.QueryAsync<MemberEffectiveDepositAggRow>(sql, new
            {
                principalRootId,
                interestRootId,
                creditsTo = creditsToExclusive,
                debitsTo = debitsToExclusive,
                activeOnly = activeMembersOnly ? 1 : 0
            })).ToList();

            return rows;
        }

        public async Task<IEnumerable<TransactionSummaryRow>> GetSummaryAsync(DateTime? from, DateTime? to, long? memberId, long? householdId)
        {
            var sql = @"
SELECT 
    g.GroupId,
    g.GroupName,
    sg.SubgroupId,
    sg.SubgroupName,
    tt.TypeId,
    tt.TypeName,
    COUNT(*) AS TxCount,
    SUM(t.AmountTotal) AS AmountTotal
FROM dbo.Transactions t
INNER JOIN dbo.TransactionTypes tt ON t.TypeId = tt.TypeId
INNER JOIN dbo.TransactionGroups g ON tt.GroupId = g.GroupId
LEFT JOIN dbo.TransactionSubgroups sg ON tt.SubgroupId = sg.SubgroupId
WHERE 1=1";
            var p = new DynamicParameters();
            if (from.HasValue)
            {
                sql += " AND t.OccurredAt >= @from";
                p.Add("from", from.Value);
            }
            if (to.HasValue)
            {
                sql += " AND t.OccurredAt < @to";
                p.Add("to", to.Value);
            }
            if (memberId.HasValue)
            {
                sql += " AND t.MemberId = @memberId";
                p.Add("memberId", memberId.Value);
            }
            if (householdId.HasValue)
            {
                sql += " AND t.HouseholdId = @householdId";
                p.Add("householdId", householdId.Value);
            }
            sql += @"
GROUP BY g.GroupId, g.GroupName, sg.SubgroupId, sg.SubgroupName, tt.TypeId, tt.TypeName
ORDER BY g.GroupName, sg.SubgroupName, tt.TypeName";

            try
            {
                return await _connection.QueryAsync<TransactionSummaryRow>(sql, p);
            }
            catch
            {
                // Fallback when TransactionTypes/Groups/Subgroups tables are not present.
                var fallbackSql = @"
SELECT
    0 AS GroupId,
    N'تراکنش‌ها' AS GroupName,
    NULL AS SubgroupId,
    NULL AS SubgroupName,
    t.TypeId,
    CASE t.TypeId
        WHEN 1 THEN N'سپرده عضویت'
        WHEN 2 THEN N'قسط وام'
        WHEN 3 THEN N'اعتبار سود'
        WHEN 4 THEN N'جریمه'
        WHEN 5 THEN N'کارمزد'
        ELSE N'سایر'
    END AS TypeName,
    COUNT(*) AS TxCount,
    SUM(t.AmountTotal) AS AmountTotal
FROM dbo.Transactions t
WHERE 1=1";
                var fp = new DynamicParameters();
                if (from.HasValue)
                {
                    fallbackSql += " AND t.OccurredAt >= @from";
                    fp.Add("from", from.Value);
                }
                if (to.HasValue)
                {
                    fallbackSql += " AND t.OccurredAt < @to";
                    fp.Add("to", to.Value);
                }
                if (memberId.HasValue)
                {
                    fallbackSql += " AND t.MemberId = @memberId";
                    fp.Add("memberId", memberId.Value);
                }
                if (householdId.HasValue)
                {
                    fallbackSql += " AND t.HouseholdId = @householdId";
                    fp.Add("householdId", householdId.Value);
                }
                fallbackSql += "\nGROUP BY t.TypeId\nORDER BY t.TypeId";

                return await _connection.QueryAsync<TransactionSummaryRow>(fallbackSql, fp);
            }
        }

        public async Task<IReadOnlyList<MemberTransactionRow>> GetMemberTransactionsAsync(long memberId, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
        {
            if (memberId <= 0) return Array.Empty<MemberTransactionRow>();
            if (limit <= 0) limit = 100;
            if (limit > 500) limit = 500;

            var sql = @"
SELECT TOP (@limit)
    t.TransactionId,
    t.OccurredAt,
    t.TypeId,
    tt.TypeName,
    t.AmountTotal,
    t.AmountPrincipal,
    t.AmountInterest,
    t.AmountPenalty,
    t.AmountFee,
    t.AmountTax,
    t.Notes
FROM dbo.Transactions t
INNER JOIN dbo.TransactionTypes tt ON tt.TypeId = t.TypeId
WHERE t.MemberId = @memberId
  AND (@fromDate IS NULL OR t.OccurredAt >= @fromDate)
  AND (@toDate IS NULL OR t.OccurredAt < DATEADD(DAY, 1, @toDate))
ORDER BY t.OccurredAt DESC, t.TransactionId DESC;";

            try
            {
                var rows = await _connection.QueryAsync<MemberTransactionRow>(sql, new { memberId, fromDate, toDate, limit });
                return rows.ToList();
            }
            catch
            {
                var fallbackSql = @"
SELECT TOP (@limit)
    t.TransactionId,
    t.OccurredAt,
    t.TypeId,
    CASE t.TypeId
        WHEN 1 THEN N'سپرده عضویت'
        WHEN 2 THEN N'قسط وام'
        WHEN 3 THEN N'اعتبار سود'
        WHEN 4 THEN N'جریمه'
        WHEN 5 THEN N'کارمزد'
        ELSE N'سایر'
    END AS TypeName,
    t.AmountTotal,
    t.AmountPrincipal,
    t.AmountInterest,
    t.AmountPenalty,
    t.AmountFee,
    t.AmountTax,
    t.Notes
FROM dbo.Transactions t
WHERE t.MemberId = @memberId
  AND (@fromDate IS NULL OR t.OccurredAt >= @fromDate)
  AND (@toDate IS NULL OR t.OccurredAt < DATEADD(DAY, 1, @toDate))
ORDER BY t.OccurredAt DESC, t.TransactionId DESC;";

                var rows = await _connection.QueryAsync<MemberTransactionRow>(fallbackSql, new { memberId, fromDate, toDate, limit });
                return rows.ToList();
            }
        }

        public async Task<IReadOnlyList<MemberSimpleTransactionRow>> GetSimpleTransactionsBySubsidiaryCodesPageAsync(
            IReadOnlyList<string> subsidiaryCodes,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int limit = 80,
            DateTime? cursorOccurredAt = null,
            long? cursorTransactionId = null,
            long? cursorJournalId = null)
        {
            if (subsidiaryCodes == null || subsidiaryCodes.Count == 0) return Array.Empty<MemberSimpleTransactionRow>();
            subsidiaryCodes = subsidiaryCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (subsidiaryCodes.Count == 0) return Array.Empty<MemberSimpleTransactionRow>();

            if (limit <= 0) limit = 80;
            if (limit > 500) limit = 500;

            var sql = @"
SELECT TOP (@limit)
    t.TransactionId,
    j.JournalId,
    CASE
        WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
            THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
        ELSE COALESCE(t.OccurredAt, j.OccurredAt)
    END AS OccurredAt,
    ISNULL(t.TypeId, 0) AS TypeId,
    ISNULL(tt.TypeName, N'سند') AS TypeName,
    (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) AS Net,
    CASE
        WHEN MAX(CASE WHEN x.RootCode IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN x.RootCode IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN SUM(CASE WHEN x.RootCode = '1301' THEN ISNULL(jl.Debit,0) ELSE 0 END)
                ELSE SUM(CASE WHEN x.RootCode IN ('1301','1302') THEN ISNULL(jl.Credit,0) ELSE 0 END)
            END
        ELSE ABS(SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0)))
    END AS Amount,
    x.MemberCode AS MemberCode,
    x.RootCodeGroup AS RootCode,
    MAX(NULLIF(jl.Description,'')) AS Description,
    ISNULL(NULLIF(t.Notes, ''), j.Notes) AS Notes
    ,CASE
        WHEN MAX(CASE WHEN x.RootCode IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN x.RootCode IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN N'پرداخت وام'
                ELSE N'واریز قسط وام'
            END
        ELSE
            CASE
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) > 0 THEN N'واریز'
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) < 0 THEN N'برداشت'
                ELSE N'-'
            END
    END AS Operation
FROM dbo.JournalLines jl
INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
INNER JOIN dbo.Accounts a1 ON a1.AccountId = jl.AccountId
INNER JOIN dbo.Accounts a2 ON a2.AccountId = jl.ParentAccountId
CROSS APPLY (
    SELECT
        CASE WHEN a1.Code IN @codes THEN a1.Code ELSE a2.Code END AS MemberCode,
        CASE WHEN a1.Code IN ('2201','2202','1301','1302') THEN a1.Code ELSE a2.Code END AS RootCode,
        CASE
            WHEN (CASE WHEN a1.Code IN ('2201','2202','1301','1302') THEN a1.Code ELSE a2.Code END) IN ('1301','1302') THEN '1301'
            ELSE (CASE WHEN a1.Code IN ('2201','2202','1301','1302') THEN a1.Code ELSE a2.Code END)
        END AS RootCodeGroup
) x
LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
LEFT JOIN dbo.TransactionTypes tt ON tt.TypeId = t.TypeId
WHERE (
        (a1.Code IN @codes AND a2.Code IN ('2201','2202','1301','1302'))
     OR (a2.Code IN @codes AND a1.Code IN ('2201','2202','1301','1302'))
      )
  AND (@fromDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) >= @fromDate)
  AND (@toDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) < DATEADD(DAY, 1, @toDate))
  AND (
        @cursorOccurredAt IS NULL
        OR (
            CASE
                WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
                    THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
                ELSE COALESCE(t.OccurredAt, j.OccurredAt)
            END
           ) < @cursorOccurredAt
        OR (
            (
                CASE
                    WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
                        THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
                    ELSE COALESCE(t.OccurredAt, j.OccurredAt)
                END
            ) = @cursorOccurredAt
            AND (
                ISNULL(t.TransactionId, 0) < ISNULL(@cursorTransactionId, 0)
                OR (ISNULL(t.TransactionId, 0) = ISNULL(@cursorTransactionId, 0) AND j.JournalId < ISNULL(@cursorJournalId, 0))
            )
        )
      )
GROUP BY t.TransactionId, j.JournalId,
    CASE
        WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
            THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
        ELSE COALESCE(t.OccurredAt, j.OccurredAt)
    END,
    ISNULL(t.TypeId, 0), ISNULL(tt.TypeName, N'سند'), x.MemberCode, x.RootCodeGroup, ISNULL(NULLIF(t.Notes, ''), j.Notes)
ORDER BY
    CASE
        WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
            THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
        ELSE COALESCE(t.OccurredAt, j.OccurredAt)
    END DESC,
    ISNULL(t.TransactionId, 0) DESC,
    j.JournalId DESC;";

            try
            {
                var rows = await _connection.QueryAsync<MemberSimpleTransactionRow>(sql, new
                {
                    codes = subsidiaryCodes,
                    fromDate,
                    toDate,
                    limit,
                    cursorOccurredAt,
                    cursorTransactionId,
                    cursorJournalId
                });
                return rows.ToList();
            }
            catch
            {
                var fallbackSql = @"
SELECT TOP (@limit)
    t.TransactionId,
    j.JournalId,
    CASE
        WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
            THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
        ELSE COALESCE(t.OccurredAt, j.OccurredAt)
    END AS OccurredAt,
    ISNULL(t.TypeId, 0) AS TypeId,
    CASE ISNULL(t.TypeId,0)
        WHEN 1 THEN N'واریز سپرده'
        WHEN 2 THEN N'پرداخت قسط وام'
        WHEN 3 THEN N'سود'
        WHEN 4 THEN N'جریمه'
        WHEN 5 THEN N'کارمزد'
        ELSE N'سند'
    END AS TypeName,
    (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) AS Net,
    CASE
        WHEN MAX(CASE WHEN x.RootCode IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN x.RootCode IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN SUM(CASE WHEN x.RootCode = '1301' THEN ISNULL(jl.Debit,0) ELSE 0 END)
                ELSE SUM(CASE WHEN x.RootCode IN ('1301','1302') THEN ISNULL(jl.Credit,0) ELSE 0 END)
            END
        ELSE ABS(SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0)))
    END AS Amount,
    x.MemberCode AS MemberCode,
    x.RootCodeGroup AS RootCode,
    MAX(NULLIF(jl.Description,'')) AS Description,
    ISNULL(NULLIF(t.Notes, ''), j.Notes) AS Notes
    ,CASE
        WHEN MAX(CASE WHEN x.RootCode IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN x.RootCode IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN N'پرداخت وام'
                ELSE N'واریز قسط وام'
            END
        ELSE
            CASE
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) > 0 THEN N'واریز'
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) < 0 THEN N'برداشت'
                ELSE N'-'
            END
    END AS Operation
FROM dbo.JournalLines jl
INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
INNER JOIN dbo.Accounts a1 ON a1.AccountId = jl.AccountId
INNER JOIN dbo.Accounts a2 ON a2.AccountId = jl.ParentAccountId
CROSS APPLY (
    SELECT
        CASE WHEN a1.Code IN @codes THEN a1.Code ELSE a2.Code END AS MemberCode,
        CASE WHEN a1.Code IN ('2201','2202','1301','1302') THEN a1.Code ELSE a2.Code END AS RootCode,
        CASE
            WHEN (CASE WHEN a1.Code IN ('2201','2202','1301','1302') THEN a1.Code ELSE a2.Code END) IN ('1301','1302') THEN '1301'
            ELSE (CASE WHEN a1.Code IN ('2201','2202','1301','1302') THEN a1.Code ELSE a2.Code END)
        END AS RootCodeGroup
) x
LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
WHERE (
        (a1.Code IN @codes AND a2.Code IN ('2201','2202','1301','1302'))
     OR (a2.Code IN @codes AND a1.Code IN ('2201','2202','1301','1302'))
      )
  AND (@fromDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) >= @fromDate)
  AND (@toDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) < DATEADD(DAY, 1, @toDate))
  AND (
        @cursorOccurredAt IS NULL
        OR (
            CASE
                WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
                    THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
                ELSE COALESCE(t.OccurredAt, j.OccurredAt)
            END
           ) < @cursorOccurredAt
        OR (
            (
                CASE
                    WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
                        THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
                    ELSE COALESCE(t.OccurredAt, j.OccurredAt)
                END
            ) = @cursorOccurredAt
            AND (
                ISNULL(t.TransactionId, 0) < ISNULL(@cursorTransactionId, 0)
                OR (ISNULL(t.TransactionId, 0) = ISNULL(@cursorTransactionId, 0) AND j.JournalId < ISNULL(@cursorJournalId, 0))
            )
        )
      )
GROUP BY t.TransactionId, j.JournalId,
    CASE
        WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
            THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
        ELSE COALESCE(t.OccurredAt, j.OccurredAt)
    END,
    ISNULL(t.TypeId, 0), x.MemberCode, x.RootCodeGroup, ISNULL(NULLIF(t.Notes, ''), j.Notes)
ORDER BY
    CASE
        WHEN CONVERT(time(0), COALESCE(t.OccurredAt, j.OccurredAt)) = '00:00:00'
            THEN COALESCE(t.CreatedAt, j.CreatedAt, t.OccurredAt, j.OccurredAt)
        ELSE COALESCE(t.OccurredAt, j.OccurredAt)
    END DESC,
    ISNULL(t.TransactionId, 0) DESC,
    j.JournalId DESC;";

                var rows = await _connection.QueryAsync<MemberSimpleTransactionRow>(fallbackSql, new
                {
                    codes = subsidiaryCodes,
                    fromDate,
                    toDate,
                    limit,
                    cursorOccurredAt,
                    cursorTransactionId,
                    cursorJournalId
                });
                return rows.ToList();
            }
        }

        public async Task<IReadOnlyList<MemberTransactionRow>> GetHouseholdTransactionsAsync(long householdId, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
        {
            if (householdId <= 0) return Array.Empty<MemberTransactionRow>();
            if (limit <= 0) limit = 100;
            if (limit > 500) limit = 500;

            var sql = @"
SELECT TOP (@limit)
    t.TransactionId,
    t.OccurredAt,
    t.TypeId,
    tt.TypeName,
    t.AmountTotal,
    t.AmountPrincipal,
    t.AmountInterest,
    t.AmountPenalty,
    t.AmountFee,
    t.AmountTax,
    t.Notes
FROM dbo.Transactions t
INNER JOIN dbo.TransactionTypes tt ON tt.TypeId = t.TypeId
WHERE t.HouseholdId = @householdId
  AND (@fromDate IS NULL OR t.OccurredAt >= @fromDate)
  AND (@toDate IS NULL OR t.OccurredAt < DATEADD(DAY, 1, @toDate))
ORDER BY t.OccurredAt DESC, t.TransactionId DESC;";

            try
            {
                var rows = await _connection.QueryAsync<MemberTransactionRow>(sql, new { householdId, fromDate, toDate, limit });
                return rows.ToList();
            }
            catch
            {
                var fallbackSql = @"
SELECT TOP (@limit)
    t.TransactionId,
    t.OccurredAt,
    t.TypeId,
    CASE t.TypeId
        WHEN 1 THEN N'سپرده عضویت'
        WHEN 2 THEN N'قسط وام'
        WHEN 3 THEN N'اعتبار سود'
        WHEN 4 THEN N'جریمه'
        WHEN 5 THEN N'کارمزد'
        ELSE N'سایر'
    END AS TypeName,
    t.AmountTotal,
    t.AmountPrincipal,
    t.AmountInterest,
    t.AmountPenalty,
    t.AmountFee,
    t.AmountTax,
    t.Notes
FROM dbo.Transactions t
WHERE t.HouseholdId = @householdId
  AND (@fromDate IS NULL OR t.OccurredAt >= @fromDate)
  AND (@toDate IS NULL OR t.OccurredAt < DATEADD(DAY, 1, @toDate))
ORDER BY t.OccurredAt DESC, t.TransactionId DESC;";

                var rows = await _connection.QueryAsync<MemberTransactionRow>(fallbackSql, new { householdId, fromDate, toDate, limit });
                return rows.ToList();
            }
        }

        public async Task<IReadOnlyList<MemberSubsidiaryJournalRow>> GetSubsidiaryJournalTransactionsAsync(
            IReadOnlyList<string> subsidiaryCodes,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int limit = 100)
        {
            if (subsidiaryCodes == null || subsidiaryCodes.Count == 0) return Array.Empty<MemberSubsidiaryJournalRow>();
            subsidiaryCodes = subsidiaryCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (subsidiaryCodes.Count == 0) return Array.Empty<MemberSubsidiaryJournalRow>();

            if (limit <= 0) limit = 100;
            if (limit > 500) limit = 500;

            var sql = @"
SELECT TOP (@limit)
    j.JournalId,
    j.OccurredAt,
    a.Code AS SubsidiaryCode,
    a.Name AS SubsidiaryName,
    SUM(ISNULL(jl.Debit,0)) AS Debit,
    SUM(ISNULL(jl.Credit,0)) AS Credit,
    SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0)) AS Net,
    j.Notes,
    j.DocumentType,
    j.TransactionType
FROM dbo.JournalLines jl
INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
INNER JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
WHERE a.Code IN @codes
  AND (@fromDate IS NULL OR j.OccurredAt >= @fromDate)
  AND (@toDate IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @toDate))
GROUP BY j.JournalId, j.OccurredAt, a.Code, a.Name, j.Notes, j.DocumentType, j.TransactionType
ORDER BY j.OccurredAt DESC, j.JournalId DESC;";

            try
            {
                var rows = await _connection.QueryAsync<MemberSubsidiaryJournalRow>(sql, new { codes = subsidiaryCodes, fromDate, toDate, limit });
                return rows.ToList();
            }
            catch (Exception ex) when (ex.Message.Contains("Invalid column name 'DocumentType'", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Invalid column name 'TransactionType'", StringComparison.OrdinalIgnoreCase))
            {
                var fallbackSql = @"
SELECT TOP (@limit)
    j.JournalId,
    j.OccurredAt,
    a.Code AS SubsidiaryCode,
    a.Name AS SubsidiaryName,
    SUM(ISNULL(jl.Debit,0)) AS Debit,
    SUM(ISNULL(jl.Credit,0)) AS Credit,
    SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0)) AS Net,
    j.Notes,
    CAST(NULL AS int) AS DocumentType,
    CAST(NULL AS nvarchar(64)) AS TransactionType
FROM dbo.JournalLines jl
INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
INNER JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
WHERE a.Code IN @codes
  AND (@fromDate IS NULL OR j.OccurredAt >= @fromDate)
  AND (@toDate IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @toDate))
GROUP BY j.JournalId, j.OccurredAt, a.Code, a.Name, j.Notes
ORDER BY j.OccurredAt DESC, j.JournalId DESC;";

                var rows = await _connection.QueryAsync<MemberSubsidiaryJournalRow>(fallbackSql, new { codes = subsidiaryCodes, fromDate, toDate, limit });
                return rows.ToList();
            }
        }

        public async Task<IReadOnlyList<MemberSimpleTransactionRow>> GetSimpleTransactionsBySubsidiaryCodesAsync(
            IReadOnlyList<string> subsidiaryCodes,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int limit = 100)
        {
            if (subsidiaryCodes == null || subsidiaryCodes.Count == 0) return Array.Empty<MemberSimpleTransactionRow>();
            subsidiaryCodes = subsidiaryCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (subsidiaryCodes.Count == 0) return Array.Empty<MemberSimpleTransactionRow>();

            if (limit <= 0) limit = 100;
            if (limit > 500) limit = 500;

            // Preferred: aggregate member subsidiary lines for deposits (2201/2202) and loan installments (1301/1302).
            // Deposit: amount/net are based on (Credit - Debit) across 2201+2202.
            // Loan installment: combine 1301+1302 into single row; if lines are Debit => Operation=پرداخت وام and Amount=only 1301;
            // if Credit => Operation=واریز قسط وام and Amount=(1301+1302).
            var sql = @"
SELECT TOP (@limit)
    t.TransactionId,
    j.JournalId,
    COALESCE(t.OccurredAt, j.OccurredAt) AS OccurredAt,
    ISNULL(t.TypeId, 0) AS TypeId,
    ISNULL(tt.TypeName, N'سند') AS TypeName,
    (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) AS Net,
    CASE
        WHEN MAX(CASE WHEN p.Code IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN p.Code IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN SUM(CASE WHEN p.Code = '1301' THEN ISNULL(jl.Debit,0) ELSE 0 END)
                ELSE SUM(CASE WHEN p.Code IN ('1301','1302') THEN ISNULL(jl.Credit,0) ELSE 0 END)
            END
        ELSE ABS(SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0)))
    END AS Amount,
    a.Code AS MemberCode,
    STRING_AGG(NULLIF(jl.Description,''), N' | ') WITHIN GROUP (ORDER BY jl.JournalLineId) AS Description,
    ISNULL(NULLIF(t.Notes, ''), j.Notes) AS Notes
    ,CASE
        WHEN MAX(CASE WHEN p.Code IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN p.Code IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN N'پرداخت وام'
                ELSE N'واریز قسط وام'
            END
        ELSE
            CASE
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) > 0 THEN N'واریز'
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) < 0 THEN N'برداشت'
                ELSE N'-'
            END
    END AS Operation
FROM dbo.JournalLines jl
INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
INNER JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
INNER JOIN dbo.Accounts p ON p.AccountId = jl.ParentAccountId
LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
LEFT JOIN dbo.TransactionTypes tt ON tt.TypeId = t.TypeId
WHERE a.Code IN @codes
  AND p.Code IN ('2201','2202','1301','1302')
  AND (@fromDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) >= @fromDate)
  AND (@toDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) < DATEADD(DAY, 1, @toDate))
GROUP BY t.TransactionId, j.JournalId, COALESCE(t.OccurredAt, j.OccurredAt), ISNULL(t.TypeId, 0), ISNULL(tt.TypeName, N'سند'), a.Code, ISNULL(NULLIF(t.Notes, ''), j.Notes)
ORDER BY COALESCE(t.OccurredAt, j.OccurredAt) DESC, ISNULL(t.TransactionId, 0) DESC, j.JournalId DESC;";

            try
            {
                var rows = await _connection.QueryAsync<MemberSimpleTransactionRow>(sql, new { codes = subsidiaryCodes, fromDate, toDate, limit });
                return rows.ToList();
            }
            catch
            {
                // Fallback: TransactionTypes table may not exist (and/or STRING_AGG unsupported).
                var fallbackSql = @"
SELECT TOP (@limit)
    t.TransactionId,
    j.JournalId,
    COALESCE(t.OccurredAt, j.OccurredAt) AS OccurredAt,
    ISNULL(t.TypeId, 0) AS TypeId,
    CASE ISNULL(t.TypeId,0)
        WHEN 1 THEN N'واریز سپرده'
        WHEN 2 THEN N'پرداخت قسط وام'
        WHEN 3 THEN N'سود'
        WHEN 4 THEN N'جریمه'
        WHEN 5 THEN N'کارمزد'
        ELSE N'سند'
    END AS TypeName,
    (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) AS Net,
    CASE
        WHEN MAX(CASE WHEN p.Code IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN p.Code IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN SUM(CASE WHEN p.Code = '1301' THEN ISNULL(jl.Debit,0) ELSE 0 END)
                ELSE SUM(CASE WHEN p.Code IN ('1301','1302') THEN ISNULL(jl.Credit,0) ELSE 0 END)
            END
        ELSE ABS(SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0)))
    END AS Amount,
    a.Code AS MemberCode,
    MAX(NULLIF(jl.Description,'')) AS Description,
    ISNULL(NULLIF(t.Notes, ''), j.Notes) AS Notes
    ,CASE
        WHEN MAX(CASE WHEN p.Code IN ('1301','1302') THEN 1 ELSE 0 END) = 1 THEN
            CASE
                WHEN SUM(CASE WHEN p.Code IN ('1301','1302') THEN ISNULL(jl.Debit,0) ELSE 0 END) > 0
                    THEN N'پرداخت وام'
                ELSE N'واریز قسط وام'
            END
        ELSE
            CASE
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) > 0 THEN N'واریز'
                WHEN (SUM(ISNULL(jl.Credit,0)) - SUM(ISNULL(jl.Debit,0))) < 0 THEN N'برداشت'
                ELSE N'-'
            END
    END AS Operation
FROM dbo.JournalLines jl
INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
INNER JOIN dbo.Accounts a ON a.AccountId = jl.AccountId
INNER JOIN dbo.Accounts p ON p.AccountId = jl.ParentAccountId
LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
WHERE a.Code IN @codes
  AND p.Code IN ('2201','2202','1301','1302')
  AND (@fromDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) >= @fromDate)
  AND (@toDate IS NULL OR COALESCE(t.OccurredAt, j.OccurredAt) < DATEADD(DAY, 1, @toDate))
GROUP BY t.TransactionId, j.JournalId, COALESCE(t.OccurredAt, j.OccurredAt), ISNULL(t.TypeId, 0), a.Code, ISNULL(NULLIF(t.Notes, ''), j.Notes)
ORDER BY COALESCE(t.OccurredAt, j.OccurredAt) DESC, ISNULL(t.TransactionId, 0) DESC, j.JournalId DESC;";

                var rows = await _connection.QueryAsync<MemberSimpleTransactionRow>(fallbackSql, new { codes = subsidiaryCodes, fromDate, toDate, limit });
                return rows.ToList();
            }
        }

        /// <summary>
        /// گزارش تراز حساب‌های اعضا با جزئیات تراکنش‌ها
        /// </summary>
        public async Task<IEnumerable<MemberBalanceDetailRow>> GetMemberBalanceDetailsAsync(long? memberId = null, long? householdId = null, DateTime? fromDate = null, DateTime? asOfDate = null)
        {
            var sql = @"
SELECT 
    m.MemberId,
    m.FirstName + ' ' + m.LastName AS MemberName,
    m.NationalCode,
    h.HouseholdId,
    h.HouseholdCode,
    h.Name AS HouseholdName,
    a.AccountId,
    a.Code AS AccountCode,
    a.Name AS AccountName,
    a.Level AS AccountLevel,
    jl.JournalLineId,
    jl.JournalId,
    j.TransactionId,
    t.OccurredAt AS TransactionDate,
    t.AmountTotal AS TransactionAmount,
    jl.Debit,
    jl.Credit,
    (jl.Debit - jl.Credit) AS NetAmount,
    pt.TemplateName AS PostingTemplate,
    CASE t.TypeId
        WHEN 1 THEN N'سپرده عضویت'
        WHEN 2 THEN N'قسط وام'
        WHEN 3 THEN N'اعتبار سود'
        WHEN 4 THEN N'جریمه'
        WHEN 5 THEN N'کارمزد'
        ELSE N'سایر'
    END AS TransactionType,
    t.Notes AS TransactionNotes,
    -- محاسبه موجودی تجمعی
    SUM(jl.Debit - jl.Credit) OVER (
        PARTITION BY m.MemberId, a.AccountId 
        ORDER BY t.OccurredAt, jl.JournalLineId
        ROWS UNBOUNDED PRECEDING
    ) AS RunningBalance
FROM JournalLines jl
INNER JOIN Journals j ON jl.JournalId = j.JournalId
INNER JOIN Transactions t ON j.TransactionId = t.TransactionId
INNER JOIN Accounts a ON jl.AccountId = a.AccountId
LEFT JOIN Members m ON jl.MemberId = m.MemberId AND m.IsActive = 1
LEFT JOIN Households h ON m.HouseholdCode = h.HouseholdCode
LEFT JOIN PostingTemplates pt ON t.PostingTemplateId = pt.PostingTemplateId
WHERE jl.MemberId IS NOT NULL";

            var parameters = new DynamicParameters();
            
            if (memberId.HasValue)
            {
                sql += " AND m.MemberId = @memberId";
                parameters.Add("memberId", memberId.Value);
            }
            
            if (householdId.HasValue)
            {
                sql += " AND h.HouseholdId = @householdId";
                parameters.Add("householdId", householdId.Value);
            }

            if (fromDate.HasValue)
            {
                sql += " AND t.OccurredAt >= @fromDate";
                parameters.Add("fromDate", fromDate.Value);
            }
            
            if (asOfDate.HasValue)
            {
                sql += " AND t.OccurredAt < DATEADD(DAY, 1, @asOfDate)";
                parameters.Add("asOfDate", asOfDate.Value);
            }
            
            sql += @"
ORDER BY 
    m.FirstName, m.LastName, 
    a.Code, 
    t.OccurredAt, 
    jl.JournalLineId";

            return await _connection.QueryAsync<MemberBalanceDetailRow>(sql, parameters);
        }

        /// <summary>
        /// گزارش خلاصه موجودی حساب‌های اعضا
        /// </summary>
        public async Task<IEnumerable<MemberBalanceSummaryRow>> GetMemberBalanceSummaryAsync(long? memberId = null, long? householdId = null, DateTime? asOfDate = null)
        {
            var sql = @"
SELECT 
    m.MemberId,
    m.FirstName + ' ' + m.LastName AS MemberName,
    m.NationalCode,
    h.HouseholdId,
    h.HouseholdCode,
    h.Name AS HouseholdName,
    a.AccountId,
    a.Code AS AccountCode,
    a.Name AS AccountName,
    a.Level AS AccountLevel,
    CASE a.Level
        WHEN 1 THEN N'گروه'
        WHEN 2 THEN N'کل'
        WHEN 3 THEN N'معین'
        ELSE N'سایر'
    END AS AccountLevelName,
    SUM(jl.Debit) AS TotalDebit,
    SUM(jl.Credit) AS TotalCredit,
    SUM(jl.Debit - jl.Credit) AS Balance,
    COUNT(jl.JournalLineId) AS TransactionCount,
    MIN(t.OccurredAt) AS FirstTransactionDate,
    MAX(t.OccurredAt) AS LastTransactionDate
FROM JournalLines jl
INNER JOIN Journals j ON jl.JournalId = j.JournalId
INNER JOIN Transactions t ON j.TransactionId = t.TransactionId
INNER JOIN Accounts a ON jl.AccountId = a.AccountId
LEFT JOIN Members m ON jl.MemberId = m.MemberId AND m.IsActive = 1
LEFT JOIN Households h ON m.HouseholdCode = h.HouseholdCode
WHERE jl.MemberId IS NOT NULL";

            var parameters = new DynamicParameters();
            
            if (memberId.HasValue)
            {
                sql += " AND m.MemberId = @memberId";
                parameters.Add("memberId", memberId.Value);
            }
            
            if (householdId.HasValue)
            {
                sql += " AND h.HouseholdId = @householdId";
                parameters.Add("householdId", householdId.Value);
            }
            
            if (asOfDate.HasValue)
            {
                sql += " AND t.OccurredAt < DATEADD(DAY, 1, @asOfDate)";
                parameters.Add("asOfDate", asOfDate.Value);
            }
            
            sql += @"
GROUP BY 
    m.MemberId, m.FirstName, m.LastName, m.NationalCode,
    h.HouseholdId, h.HouseholdCode, h.Name,
    a.AccountId, a.Code, a.Name, a.Level
HAVING SUM(jl.Debit - jl.Credit) != 0  -- فقط حساب‌هایی که موجودی دارند
ORDER BY 
    m.FirstName, m.LastName, 
    a.Code";

            return await _connection.QueryAsync<MemberBalanceSummaryRow>(sql, parameters);
        }

        /// <summary>
        /// گزارش موجودی بانکی اعضا (نمایش مثبت)
        /// </summary>
        public async Task<IEnumerable<MemberBankingBalanceRow>> GetMemberBankingBalancesAsync(long? memberId = null, long? householdId = null)
        {
            var sql = @"
SELECT 
    m.MemberId,
    m.FirstName + ' ' + m.LastName AS MemberName,
    m.NationalCode,
    m.HouseholdCode,
    h.Name AS HouseholdName,
    m.JoinedAt,
    -- موجودی اصل سپرده (حساب 2201)
    ISNULL(ABS(SUM(CASE WHEN a.Code = '2201' THEN (jl.Credit - jl.Debit) ELSE 0 END)), 0) AS PrincipalBalance,
    -- موجودی سود (حساب 2202)  
    ISNULL(ABS(SUM(CASE WHEN a.Code = '2202' THEN (jl.Credit - jl.Debit) ELSE 0 END)), 0) AS InterestBalance,
    -- کل موجودی
    ISNULL(ABS(SUM(jl.Credit - jl.Debit)), 0) AS TotalBalance,
    -- تعداد تراکنش
    COUNT(DISTINCT t.TransactionId) AS TransactionCount,
    -- آخرین تراکنش
    MAX(t.OccurredAt) AS LastTransactionDate
FROM Members m
LEFT JOIN Households h ON m.HouseholdCode = h.HouseholdCode
LEFT JOIN JournalLines jl ON jl.MemberId = m.MemberId
LEFT JOIN Journals j ON jl.JournalId = j.JournalId
LEFT JOIN Transactions t ON j.TransactionId = t.TransactionId
LEFT JOIN Accounts a ON jl.AccountId = a.AccountId
WHERE m.IsActive = 1";

            var parameters = new DynamicParameters();
            
            if (memberId.HasValue)
            {
                sql += " AND m.MemberId = @memberId";
                parameters.Add("memberId", memberId.Value);
            }
            
            if (householdId.HasValue)
            {
                sql += " AND h.HouseholdId = @householdId";
                parameters.Add("householdId", householdId.Value);
            }
            
            sql += @"
GROUP BY 
    m.MemberId, m.FirstName, m.LastName, m.NationalCode,
    m.HouseholdCode, h.Name, m.JoinedAt
ORDER BY 
    TotalBalance DESC, m.FirstName, m.LastName";

            return await _connection.QueryAsync<MemberBankingBalanceRow>(sql, parameters);
        }

        public async Task<IEnumerable<MemberBankingBalanceRow>> GetHouseholdMemberBankingBalancesAsync(string householdCode)
        {
            // We key member balances by the subsidiary code (M-xxxx). The deposit amounts may be posted
            // on various descendant accounts under 2201/2202, so we include the full subtree.
            const string rootSql = @"
SELECT
  MAX(CASE WHEN Code = '2201' THEN AccountId END) AS PrincipalRootId,
  MAX(CASE WHEN Code = '2202' THEN AccountId END) AS InterestRootId
FROM dbo.Accounts
WHERE IsActive = 1 AND Code IN ('2201','2202');";

            var roots = await _connection.QuerySingleOrDefaultAsync<(long? PrincipalRootId, long? InterestRootId)>(rootSql);
            var principalRootId = roots.PrincipalRootId ?? 0;
            var interestRootId = roots.InterestRootId ?? 0;
            if (principalRootId <= 0 || interestRootId <= 0)
                return Array.Empty<MemberBankingBalanceRow>();

            var sql = @"
;WITH principal_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @principalRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN principal_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
interest_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @interestRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN interest_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
)
SELECT
  m.MemberId,
  sub.Code AS BankAccountNumber,
  m.FirstName + ' ' + m.LastName AS MemberName,
  m.NationalCode,
  m.HouseholdCode,
  h.Name AS HouseholdName,
  m.JoinedAt,

  ISNULL(agg.PrincipalCredit, 0) AS PrincipalBalance,
  ISNULL(agg.PrincipalDebit, 0) AS PrincipalDebit,
  ISNULL(agg.InterestCredit, 0) AS InterestBalance,
  ISNULL(agg.InterestDebit, 0) AS InterestDebit,
  ISNULL(agg.TransactionCount, 0) AS TransactionCount,
  agg.LastTransactionDate AS LastTransactionDate
FROM Members m
LEFT JOIN Households h ON m.HouseholdCode = h.HouseholdCode
OUTER APPLY (
    -- Subsidiary code used as the public identifier (e.g., M-11008)
    SELECT TOP 1 a2.Code, a2.AccountId
    FROM Accounts a2
    WHERE a2.MemberId = m.MemberId
      AND a2.IsActive = 1
      AND a2.Code LIKE 'M-%'
    ORDER BY a2.AccountId DESC
) sub

OUTER APPLY (
    SELECT
      SUM(CASE WHEN j.JournalId IS NOT NULL AND pt.AccountId IS NOT NULL
               THEN ISNULL(jl.Credit,0) ELSE 0 END) AS PrincipalCredit,
      SUM(CASE WHEN j.JournalId IS NOT NULL AND pt.AccountId IS NOT NULL
               THEN ISNULL(jl.Debit,0) ELSE 0 END) AS PrincipalDebit,
      SUM(CASE WHEN j.JournalId IS NOT NULL AND it.AccountId IS NOT NULL
               THEN ISNULL(jl.Credit,0) ELSE 0 END) AS InterestCredit,
      SUM(CASE WHEN j.JournalId IS NOT NULL AND it.AccountId IS NOT NULL
               THEN ISNULL(jl.Debit,0) ELSE 0 END) AS InterestDebit,
      COUNT(DISTINCT t.TransactionId) AS TransactionCount,
      MAX(t.OccurredAt) AS LastTransactionDate
    FROM dbo.JournalLines jl
    LEFT JOIN principal_tree pt ON jl.ParentAccountId = pt.AccountId
    LEFT JOIN interest_tree it ON jl.ParentAccountId = it.AccountId
    LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
    LEFT JOIN dbo.Transactions t ON j.TransactionId = t.TransactionId
    WHERE sub.AccountId IS NOT NULL
      AND jl.AccountId = sub.AccountId
) agg
WHERE m.IsActive = 1 AND m.HouseholdCode = @householdCode
ORDER BY m.MemberId ASC;";

            // Use a temp projection to compute net the same way ManualJournalEntry does:
            // net = (principalCredit + interestCredit) - (principalDebit + interestDebit)
            var rows = (await _connection.QueryAsync<HouseholdMemberDepositAgg>(
                sql,
                new { householdCode, principalRootId, interestRootId }))
                .ToList();

            return rows.Select(r => new MemberBankingBalanceRow
            {
                MemberId = r.MemberId,
                BankAccountNumber = r.BankAccountNumber,
                MemberName = r.MemberName,
                NationalCode = r.NationalCode,
                HouseholdCode = r.HouseholdCode,
                HouseholdName = r.HouseholdName,
                JoinedAt = r.JoinedAt,
                PrincipalBalance = r.PrincipalBalance - r.PrincipalDebit,
                InterestBalance = r.InterestBalance - r.InterestDebit,
                TotalBalance = (r.PrincipalBalance - r.PrincipalDebit) + (r.InterestBalance - r.InterestDebit),
                TransactionCount = r.TransactionCount,
                LastTransactionDate = r.LastTransactionDate
            });
        }

        public async Task<decimal> GetMemberNetCapitalByMemberIdAsync(long memberId, IDbTransaction? tx = null)
        {
            if (memberId <= 0) return 0m;

            const string accountIdsSql = @"
SELECT a.Code, a.AccountId
FROM dbo.Accounts a
WHERE a.IsActive = 1 AND a.Level = 3 AND a.Code IN ('2201','2202');";

            var accountIds = (await _connection.QueryAsync<(string Code, long AccountId)>(accountIdsSql, transaction: tx)).ToList();
            var principalAccountId = accountIds.FirstOrDefault(x => x.Code == "2201").AccountId;
            var interestAccountId = accountIds.FirstOrDefault(x => x.Code == "2202").AccountId;

            if (principalAccountId <= 0 || interestAccountId <= 0)
                return 0m;

            const string sql = @"
SELECT
  SUM(CASE WHEN (a.ParentAccountId = @principalAccountId OR jl.AccountId = @principalAccountId)
           THEN ISNULL(jl.Credit,0) ELSE 0 END) AS PrincipalCredit,
  SUM(CASE WHEN (a.ParentAccountId = @principalAccountId OR jl.AccountId = @principalAccountId)
           THEN ISNULL(jl.Debit,0) ELSE 0 END) AS PrincipalDebit,
  SUM(CASE WHEN (a.ParentAccountId = @interestAccountId OR jl.AccountId = @interestAccountId)
           THEN ISNULL(jl.Credit,0) ELSE 0 END) AS InterestCredit,
  SUM(CASE WHEN (a.ParentAccountId = @interestAccountId OR jl.AccountId = @interestAccountId)
           THEN ISNULL(jl.Debit,0) ELSE 0 END) AS InterestDebit
FROM dbo.JournalLines jl
LEFT JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
WHERE (jl.MemberId = @memberId OR a.MemberId = @memberId);";

            var r = await _connection.QuerySingleOrDefaultAsync<MemberDepositAgg>(
                sql,
                new { memberId, principalAccountId, interestAccountId },
                transaction: tx);

            if (r == null) return 0m;
            return (r.PrincipalCredit + r.InterestCredit) - (r.PrincipalDebit + r.InterestDebit);
        }

        public async Task<decimal> GetMemberNetCapitalBySubsidiaryCodeAsync(string memberSubsidiaryCode, IDbTransaction? tx = null)
        {
            if (string.IsNullOrWhiteSpace(memberSubsidiaryCode)) return 0m;

            const string rootSql = @"
SELECT
  MAX(CASE WHEN Code = '2201' THEN AccountId END) AS PrincipalRootId,
  MAX(CASE WHEN Code = '2202' THEN AccountId END) AS InterestRootId
FROM dbo.Accounts
WHERE IsActive = 1 AND Code IN ('2201','2202');";

            var roots = await _connection.QuerySingleOrDefaultAsync<(long? PrincipalRootId, long? InterestRootId)>(rootSql, transaction: tx);
            var principalRootId = roots.PrincipalRootId ?? 0;
            var interestRootId = roots.InterestRootId ?? 0;
            if (principalRootId <= 0 || interestRootId <= 0)
                return 0m;

            const string sql = @"
;WITH principal_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @principalRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN principal_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
interest_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @interestRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN interest_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
sub AS (
    SELECT TOP 1 a.AccountId
    FROM dbo.Accounts a
    WHERE a.IsActive = 1 AND a.Code = @memberSubsidiaryCode
    ORDER BY a.AccountId DESC
)
SELECT
  SUM(CASE WHEN pt.AccountId IS NOT NULL
           THEN ISNULL(jl.Credit,0) ELSE 0 END) AS PrincipalCredit,
  SUM(CASE WHEN pt.AccountId IS NOT NULL
           THEN ISNULL(jl.Debit,0) ELSE 0 END) AS PrincipalDebit,
  SUM(CASE WHEN it.AccountId IS NOT NULL
           THEN ISNULL(jl.Credit,0) ELSE 0 END) AS InterestCredit,
  SUM(CASE WHEN it.AccountId IS NOT NULL
           THEN ISNULL(jl.Debit,0) ELSE 0 END) AS InterestDebit
FROM dbo.JournalLines jl
LEFT JOIN principal_tree pt ON jl.ParentAccountId = pt.AccountId
LEFT JOIN interest_tree it ON jl.ParentAccountId = it.AccountId
WHERE jl.AccountId = (SELECT AccountId FROM sub);";

            var r = await _connection.QuerySingleOrDefaultAsync<MemberDepositAgg>(
                sql,
                new { memberSubsidiaryCode, principalRootId, interestRootId },
                transaction: tx);

            if (r == null) return 0m;
            return (r.PrincipalCredit + r.InterestCredit) - (r.PrincipalDebit + r.InterestDebit);
        }

        public async Task<MemberDepositAggDto> GetMemberDepositAggBySubsidiaryCodeAsync(string memberSubsidiaryCode, IDbTransaction? tx = null)
        {
            if (string.IsNullOrWhiteSpace(memberSubsidiaryCode)) return new MemberDepositAggDto();

            const string rootSql = @"
SELECT
  MAX(CASE WHEN Code = '2201' THEN AccountId END) AS PrincipalRootId,
  MAX(CASE WHEN Code = '2202' THEN AccountId END) AS InterestRootId
FROM dbo.Accounts
WHERE IsActive = 1 AND Code IN ('2201','2202');";

            var roots = await _connection.QuerySingleOrDefaultAsync<(long? PrincipalRootId, long? InterestRootId)>(rootSql, transaction: tx);
            var principalRootId = roots.PrincipalRootId ?? 0;
            var interestRootId = roots.InterestRootId ?? 0;
            if (principalRootId <= 0 || interestRootId <= 0)
                return new MemberDepositAggDto();

            const string sql = @"
;WITH principal_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @principalRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN principal_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
interest_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @interestRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN interest_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
sub AS (
    SELECT TOP 1 a.AccountId
    FROM dbo.Accounts a
    WHERE a.IsActive = 1 AND a.Code = @memberSubsidiaryCode
    ORDER BY a.AccountId DESC
)
SELECT
  SUM(CASE WHEN pt.AccountId IS NOT NULL THEN ISNULL(jl.Credit,0) ELSE 0 END) AS PrincipalCredit,
  SUM(CASE WHEN pt.AccountId IS NOT NULL THEN ISNULL(jl.Debit,0) ELSE 0 END) AS PrincipalDebit,
  SUM(CASE WHEN it.AccountId IS NOT NULL THEN ISNULL(jl.Credit,0) ELSE 0 END) AS InterestCredit,
  SUM(CASE WHEN it.AccountId IS NOT NULL THEN ISNULL(jl.Debit,0) ELSE 0 END) AS InterestDebit
FROM dbo.JournalLines jl
LEFT JOIN principal_tree pt ON jl.ParentAccountId = pt.AccountId
LEFT JOIN interest_tree it ON jl.ParentAccountId = it.AccountId
WHERE jl.AccountId = (SELECT AccountId FROM sub);";

            var r = await _connection.QuerySingleOrDefaultAsync<MemberDepositAggDto>(
                sql,
                new { memberSubsidiaryCode, principalRootId, interestRootId },
                transaction: tx);

            return r ?? new MemberDepositAggDto();
        }

        public async Task<IReadOnlyDictionary<string, MemberDepositAggDto>> GetMemberDepositAggBySubsidiaryCodesAsync(IEnumerable<string> memberSubsidiaryCodes, IDbTransaction? tx = null)
        {
            if (memberSubsidiaryCodes == null)
                return new Dictionary<string, MemberDepositAggDto>(StringComparer.OrdinalIgnoreCase);

            var codes = memberSubsidiaryCodes
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codes.Count == 0)
                return new Dictionary<string, MemberDepositAggDto>(StringComparer.OrdinalIgnoreCase);

            const string rootSql = @"
SELECT
  MAX(CASE WHEN Code = '2201' THEN AccountId END) AS PrincipalRootId,
  MAX(CASE WHEN Code = '2202' THEN AccountId END) AS InterestRootId
FROM dbo.Accounts
WHERE IsActive = 1 AND Code IN ('2201','2202');";

            var roots = await _connection.QuerySingleOrDefaultAsync<(long? PrincipalRootId, long? InterestRootId)>(rootSql, transaction: tx);
            var principalRootId = roots.PrincipalRootId ?? 0;
            var interestRootId = roots.InterestRootId ?? 0;
            if (principalRootId <= 0 || interestRootId <= 0)
                return new Dictionary<string, MemberDepositAggDto>(StringComparer.OrdinalIgnoreCase);

            const string sql = @"
;WITH principal_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @principalRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN principal_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
interest_tree AS (
    SELECT AccountId FROM dbo.Accounts WHERE AccountId = @interestRootId
    UNION ALL
    SELECT a.AccountId
    FROM dbo.Accounts a
    INNER JOIN interest_tree p ON a.ParentAccountId = p.AccountId
    WHERE a.IsActive = 1
),
subs AS (
    SELECT a.Code, MAX(a.AccountId) AS AccountId
    FROM dbo.Accounts a
    WHERE a.IsActive = 1 AND a.Code IN @codes
    GROUP BY a.Code
)
SELECT
  s.Code AS MemberCode,
  SUM(CASE WHEN pt.AccountId IS NOT NULL THEN ISNULL(jl.Credit,0) ELSE 0 END) AS PrincipalCredit,
  SUM(CASE WHEN pt.AccountId IS NOT NULL THEN ISNULL(jl.Debit,0) ELSE 0 END) AS PrincipalDebit,
  SUM(CASE WHEN it.AccountId IS NOT NULL THEN ISNULL(jl.Credit,0) ELSE 0 END) AS InterestCredit,
  SUM(CASE WHEN it.AccountId IS NOT NULL THEN ISNULL(jl.Debit,0) ELSE 0 END) AS InterestDebit
FROM subs s
LEFT JOIN dbo.JournalLines jl ON jl.AccountId = s.AccountId
LEFT JOIN principal_tree pt ON jl.ParentAccountId = pt.AccountId
LEFT JOIN interest_tree it ON jl.ParentAccountId = it.AccountId
GROUP BY s.Code;";

            var rows = (await _connection.QueryAsync<MemberDepositAggRow>(
                sql,
                new { codes, principalRootId, interestRootId },
                transaction: tx)).ToList();

            var dict = new Dictionary<string, MemberDepositAggDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.MemberCode)) continue;
                dict[r.MemberCode] = new MemberDepositAggDto
                {
                    PrincipalCredit = r.PrincipalCredit,
                    PrincipalDebit = r.PrincipalDebit,
                    InterestCredit = r.InterestCredit,
                    InterestDebit = r.InterestDebit
                };
            }
            return dict;
        }

        private sealed class MemberDepositAgg
        {
            public decimal PrincipalCredit { get; set; }
            public decimal PrincipalDebit { get; set; }
            public decimal InterestCredit { get; set; }
            public decimal InterestDebit { get; set; }
        }

        public sealed class MemberDepositAggDto
        {
            public decimal PrincipalCredit { get; set; }
            public decimal PrincipalDebit { get; set; }
            public decimal InterestCredit { get; set; }
            public decimal InterestDebit { get; set; }
        }

        private sealed class MemberDepositAggRow
        {
            public string? MemberCode { get; set; }
            public decimal PrincipalCredit { get; set; }
            public decimal PrincipalDebit { get; set; }
            public decimal InterestCredit { get; set; }
            public decimal InterestDebit { get; set; }
        }

        private sealed class HouseholdMemberDepositAgg
        {
            public long MemberId { get; set; }
            public string? BankAccountNumber { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public string NationalCode { get; set; } = string.Empty;
            public string HouseholdCode { get; set; } = string.Empty;
            public string HouseholdName { get; set; } = string.Empty;
            public DateTime JoinedAt { get; set; }
            public decimal PrincipalBalance { get; set; }
            public decimal PrincipalDebit { get; set; }
            public decimal InterestBalance { get; set; }
            public decimal InterestDebit { get; set; }
            public int TransactionCount { get; set; }
            public DateTime? LastTransactionDate { get; set; }
        }

        /// <summary>
        /// جزئیات موجودی بانکی عضو به تفکیک حساب
        /// </summary>
        public async Task<IEnumerable<MemberBankingDetailRow>> GetMemberBankingDetailsAsync(long? memberId = null, long? householdId = null)
        {
            var sql = @"
SELECT 
    m.MemberId,
    m.FirstName + ' ' + m.LastName AS MemberName,
    m.NationalCode,
    m.HouseholdCode,
    h.Name AS HouseholdName,
    a.Code AS AccountCode,
    a.Name AS AccountName,
    CASE 
        WHEN a.Code = '2201' THEN N'سپرده اصل'
        WHEN a.Code = '2202' THEN N'سود سپرده'
        ELSE N'سایر'
    END AS AccountType,
    -- نمایش موجودی به صورت مثبت
    ABS(SUM(jl.Credit - jl.Debit)) AS Balance,
    COUNT(jl.JournalLineId) AS TransactionCount,
    MIN(t.OccurredAt) AS FirstTransactionDate,
    MAX(t.OccurredAt) AS LastTransactionDate
FROM Members m
LEFT JOIN Households h ON m.HouseholdCode = h.HouseholdCode
LEFT JOIN JournalLines jl ON jl.MemberId = m.MemberId
LEFT JOIN Journals j ON jl.JournalId = j.JournalId
LEFT JOIN Transactions t ON j.TransactionId = t.TransactionId
LEFT JOIN Accounts a ON jl.AccountId = a.AccountId
WHERE m.IsActive = 1 AND jl.MemberId IS NOT NULL";

            var parameters = new DynamicParameters();
            
            if (memberId.HasValue)
            {
                sql += " AND m.MemberId = @memberId";
                parameters.Add("memberId", memberId.Value);
            }
            
            if (householdId.HasValue)
            {
                sql += " AND h.HouseholdId = @householdId";
                parameters.Add("householdId", householdId.Value);
            }
            
            sql += @"
GROUP BY 
    m.MemberId, m.FirstName, m.LastName, m.NationalCode,
    m.HouseholdCode, h.Name, a.AccountId, a.Code, a.Name
HAVING SUM(jl.Credit - jl.Debit) != 0
ORDER BY 
    m.FirstName, m.LastName, a.Code";

            return await _connection.QueryAsync<MemberBankingDetailRow>(sql, parameters);
        }

        /// <summary>
        /// خلاصه کل صندوق
        /// </summary>
        public async Task<FundSummaryRow> GetFundSummaryAsync()
        {
            var sql = @"
SELECT 
    COUNT(DISTINCT m.MemberId) AS TotalMembers,
    COUNT(DISTINCT CASE WHEN m.IsActive = 1 THEN m.MemberId END) AS ActiveMembers,
    ISNULL(ABS(SUM(CASE WHEN a.Code = '2201' AND m.IsActive = 1 THEN (jl.Credit - jl.Debit) ELSE 0 END)), 0) AS TotalPrincipalBalance,
    ISNULL(ABS(SUM(CASE WHEN a.Code = '2202' AND m.IsActive = 1 THEN (jl.Credit - jl.Debit) ELSE 0 END)), 0) AS TotalInterestBalance,
    ISNULL(ABS(SUM(CASE WHEN m.IsActive = 1 THEN (jl.Credit - jl.Debit) ELSE 0 END)), 0) AS TotalFundBalance,
    COUNT(DISTINCT t.TransactionId) AS TotalTransactions,
    GETDATE() AS LastUpdated
FROM Members m
LEFT JOIN JournalLines jl ON jl.MemberId = m.MemberId
LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
LEFT JOIN Transactions t ON j.TransactionId = t.TransactionId
LEFT JOIN Accounts a ON jl.AccountId = a.AccountId";

            return await _connection.QueryFirstAsync<FundSummaryRow>(sql);
        }

        /// <summary>
        /// گزارش ردیابی مسیر تراکنش (Transaction Trail)
        /// </summary>
        public async Task<TransactionTrailReport> GetTransactionTrailAsync(long transactionId)
        {
            // اطلاعات اصلی تراکنش
            var transactionSql = @"
SELECT 
    t.TransactionId,
    t.TypeId,
    t.HouseholdId,
    t.MemberId,
    t.AmountTotal,
    t.AmountPrincipal,
    t.AmountInterest,
    t.AmountFee,
    t.AmountTax,
    t.AmountPenalty,
    t.OccurredAt,
    t.CreatedAt,
    t.CreatedBy,
    t.Status,
    t.Notes,
    t.ProcessId,
    t.PostingTemplateId,
    t.ParentReceiptId,
    t.AllocationId,
    h.HouseholdCode,
    h.Name AS HouseholdName,
    m.FirstName + ' ' + m.LastName AS MemberName,
    m.NationalCode,
    pt.TemplateName AS PostingTemplateName,
    hr.HouseholdReceiptId,
    hr.AmountTotal AS ReceiptAmount,
    CASE t.TypeId
        WHEN 1 THEN N'سپرده عضویت'
        WHEN 2 THEN N'قسط وام'
        WHEN 3 THEN N'اعتبار سود'
        WHEN 4 THEN N'جریمه'
        WHEN 5 THEN N'کارمزد'
        ELSE N'سایر'
    END AS TransactionTypeName
FROM Transactions t
LEFT JOIN Households h ON t.HouseholdId = h.HouseholdId
LEFT JOIN Members m ON t.MemberId = m.MemberId AND m.IsActive = 1
LEFT JOIN PostingTemplates pt ON t.PostingTemplateId = pt.PostingTemplateId
LEFT JOIN HouseholdReceipts hr ON t.ParentReceiptId = hr.HouseholdReceiptId
WHERE t.TransactionId = @transactionId";

            var transaction = await _connection.QueryFirstOrDefaultAsync<TransactionTrailInfo>(transactionSql, new { transactionId });
            
            if (transaction == null)
                return new TransactionTrailReport { TransactionId = transactionId };

            // جزئیات ثبت‌های حسابداری
            var journalLinesSql = @"
SELECT 
    jl.JournalLineId,
    jl.JournalId,
    jl.[LineNo],
    jl.AccountId,
    jl.MemberId,
    jl.Debit,
    jl.Credit,
    a.Code AS AccountCode,
    a.Name AS AccountName,
    a.Level AS AccountLevel,
    CASE a.Level
        WHEN 1 THEN N'گروه'
        WHEN 2 THEN N'کل'
        WHEN 3 THEN N'معین'
        ELSE N'سایر'
    END AS AccountLevelName,
    CASE 
        WHEN jl.MemberId IS NOT NULL THEN m.FirstName + ' ' + m.LastName
        ELSE NULL
    END AS MemberName,
    CASE 
        WHEN jl.MemberId IS NOT NULL THEN m.NationalCode
        ELSE NULL
    END AS MemberNationalCode
FROM JournalLines jl
INNER JOIN Journals j ON jl.JournalId = j.JournalId
INNER JOIN Accounts a ON jl.AccountId = a.AccountId
LEFT JOIN Members m ON jl.MemberId = m.MemberId AND m.IsActive = 1
WHERE j.TransactionId = @transactionId
ORDER BY jl.[LineNo]";

            var journalLines = await _connection.QueryAsync<TransactionJournalLineInfo>(journalLinesSql, new { transactionId });

            // تاریخچه پردازش (Process Log)
            var processLogSql = @"
SELECT 
    pl.LogId,
    pl.ProcessId,
    pl.Step,
    pl.Status,
    pl.Message,
    pl.CreatedAt,
    pl.CreatedBy
FROM ProcessLog pl
WHERE pl.RelatedId = @transactionId
   OR pl.ProcessId = @processId
ORDER BY pl.CreatedAt";

            var processLogs = await _connection.QueryAsync<ProcessLogInfo>(processLogSql, new { transactionId, processId = transaction.ProcessId });

            return new TransactionTrailReport
            {
                TransactionId = transactionId,
                Transaction = transaction,
                JournalLines = journalLines.ToList(),
                ProcessLogs = processLogs.ToList()
            };
        }

        /// <summary>
        /// گزارش تأثیر تراکنش‌ها بر حساب‌های مختلف
        /// </summary>
        public async Task<IEnumerable<TransactionImpactRow>> GetTransactionImpactAsync(DateTime? fromDate = null, DateTime? toDate = null, long? accountId = null)
        {
            var sql = @"
SELECT 
    a.AccountId,
    a.Code AS AccountCode,
    a.Name AS AccountName,
    a.Level AS AccountLevel,
    COUNT(DISTINCT t.TransactionId) AS TransactionCount,
    COUNT(jl.JournalLineId) AS JournalLineCount,
    SUM(jl.Debit) AS TotalDebit,
    SUM(jl.Credit) AS TotalCredit,
    SUM(jl.Debit - jl.Credit) AS NetImpact,
    COUNT(DISTINCT jl.MemberId) AS AffectedMemberCount,
    MIN(t.OccurredAt) AS FirstTransactionDate,
    MAX(t.OccurredAt) AS LastTransactionDate,
    -- تفکیک بر اساس نوع تراکنش
    SUM(CASE WHEN t.TypeId = 1 THEN jl.Debit - jl.Credit ELSE 0 END) AS DepositImpact,
    SUM(CASE WHEN t.TypeId = 2 THEN jl.Debit - jl.Credit ELSE 0 END) AS LoanPaymentImpact,
    SUM(CASE WHEN t.TypeId = 3 THEN jl.Debit - jl.Credit ELSE 0 END) AS InterestImpact,
    SUM(CASE WHEN t.TypeId = 4 THEN jl.Debit - jl.Credit ELSE 0 END) AS PenaltyImpact,
    SUM(CASE WHEN t.TypeId = 5 THEN jl.Debit - jl.Credit ELSE 0 END) AS FeeImpact
FROM Accounts a
INNER JOIN JournalLines jl ON a.AccountId = jl.AccountId
INNER JOIN Journals j ON jl.JournalId = j.JournalId
INNER JOIN Transactions t ON j.TransactionId = t.TransactionId
WHERE 1=1";

            var parameters = new DynamicParameters();
            
            if (fromDate.HasValue)
            {
                sql += " AND t.OccurredAt >= @fromDate";
                parameters.Add("fromDate", fromDate.Value);
            }
            
            if (toDate.HasValue)
            {
                sql += " AND t.OccurredAt < DATEADD(DAY, 1, @toDate)";
                parameters.Add("toDate", toDate.Value);
            }
            
            if (accountId.HasValue)
            {
                sql += " AND a.AccountId = @accountId";
                parameters.Add("accountId", accountId.Value);
            }
            
            sql += @"
GROUP BY a.AccountId, a.Code, a.Name, a.Level
HAVING COUNT(jl.JournalLineId) > 0
ORDER BY ABS(SUM(jl.Debit - jl.Credit)) DESC, a.Code";

            return await _connection.QueryAsync<TransactionImpactRow>(sql, parameters);
        }
    }
}
