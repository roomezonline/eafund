using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class LoanReportsRepository
    {
        private readonly IDbConnection _db;

        public LoanReportsRepository(IDbConnection db)
        {
            _db = db;
        }

        public sealed class LoanSummaryReportRow
        {
            public long LoanId { get; set; }
            public long MemberId { get; set; }
            public string? MemberCode { get; set; }
            public string MemberFullName { get; set; } = string.Empty;
            public byte Status { get; set; }
            public decimal PrincipalAmount { get; set; }
            public decimal InterestAmount { get; set; }
            public decimal TotalLoanAmount { get; set; }

            public decimal RemainingPrincipal { get; set; }
            public decimal RemainingInterest { get; set; }
            public decimal RemainingTotal { get; set; }

            public int TotalInstallments { get; set; }
            public int PaidInstallments { get; set; }
            public DateTime? LastPaidInstallmentDueDate { get; set; }

            public int OverdueInstallments { get; set; }
            public decimal OverdueAmountNoPenalty { get; set; }
            public string? OverdueDueDatesText { get; set; }

            public decimal CurrentInstallmentInterest { get; set; }
            public decimal CurrentAndOverdueInstallmentsInterest { get; set; }
        }

        public async Task<IReadOnlyList<LoanSummaryReportRow>> GetLoansSummaryReportAsync(DateTime asOfDate)
        {
            const string sql = @"
SELECT
    l.LoanId,
    l.MemberId,
    l.MemberCode,
    (m.FirstName + ' ' + m.LastName) AS MemberFullName,

    l.Status,

    l.Principal AS PrincipalAmount,
    ISNULL(interestAgg.SumInterest, 0) AS InterestAmount,
    (l.Principal + ISNULL(interestAgg.SumInterest, 0)) AS TotalLoanAmount,

    ISNULL(remainingAgg.RemainingPrincipal, 0) AS RemainingPrincipal,
    ISNULL(remainingAgg.RemainingInterest, 0) AS RemainingInterest,
    (ISNULL(remainingAgg.RemainingPrincipal, 0) + ISNULL(remainingAgg.RemainingInterest, 0)) AS RemainingTotal,

    ISNULL(instAgg.TotalInstallments, 0) AS TotalInstallments,
    ISNULL(instAgg.PaidInstallments, 0) AS PaidInstallments,
    lastPaid.LastPaidInstallmentDueDate,

    ISNULL(overdueAgg.OverdueInstallments, 0) AS OverdueInstallments,
    ISNULL(overdueAgg.OverdueAmountNoPenalty, 0) AS OverdueAmountNoPenalty,
    overdueAgg.OverdueDueDatesText,

    -- Current Interest logic:
    -- If there are overdue installments, the 'Current' interest to display in the top line 
    -- should be the interest of the first overdue installment (the one that needs payment now).
    -- If no overdue, it shows the next future installment interest.
    CASE 
        WHEN ISNULL(overdueAgg.OverdueInstallments, 0) > 0 THEN ISNULL(overdueAgg.FirstOverdueInterest, 0)
        ELSE ISNULL(currentAgg.CurrentInstallmentInterest, 0)
    END AS CurrentInstallmentInterest,

    CASE 
        WHEN ISNULL(overdueAgg.OverdueInstallments, 0) > 0 THEN ISNULL(overdueAgg.OverdueInterestNoPenalty, 0)
        ELSE ISNULL(currentAgg.CurrentInstallmentInterest, 0) + ISNULL(overdueAgg.OverdueInterestNoPenalty, 0)
    END AS CurrentAndOverdueInstallmentsInterest

FROM dbo.Loans l
INNER JOIN dbo.Members m ON m.MemberId = l.MemberId

OUTER APPLY (
    SELECT SUM(i.InterestAmount) AS SumInterest
    FROM dbo.LoanInstallments i
    WHERE i.LoanId = l.LoanId
) interestAgg

OUTER APPLY (
    SELECT
        SUM(CASE WHEN i.PrincipalAmount - i.PaidPrincipal > 0 THEN (i.PrincipalAmount - i.PaidPrincipal) ELSE 0 END) AS RemainingPrincipal,
        SUM(CASE WHEN i.InterestAmount - i.PaidInterest > 0 THEN (i.InterestAmount - i.PaidInterest) ELSE 0 END) AS RemainingInterest
    FROM dbo.LoanInstallments i
    WHERE i.LoanId = l.LoanId
) remainingAgg

OUTER APPLY (
    SELECT
        COUNT(1) AS TotalInstallments,
        SUM(CASE WHEN i.Status = 3 THEN 1 ELSE 0 END) AS PaidInstallments
    FROM dbo.LoanInstallments i
    WHERE i.LoanId = l.LoanId
) instAgg

OUTER APPLY (
    SELECT MAX(i.DueDate) AS LastPaidInstallmentDueDate
    FROM dbo.LoanInstallments i
    WHERE i.LoanId = l.LoanId
      AND i.Status = 3
) lastPaid

OUTER APPLY (
    SELECT TOP 1
        CASE
            WHEN (i.InterestAmount - i.PaidInterest) > 0 THEN (i.InterestAmount - i.PaidInterest)
            ELSE 0
        END AS CurrentInstallmentInterest
    FROM dbo.LoanInstallments i
    WHERE i.LoanId = l.LoanId
      AND i.DueDate >= @asOfDate
      AND i.Status <> 3
    ORDER BY i.DueDate ASC
) currentAgg

OUTER APPLY (
    SELECT
        COUNT(1) AS OverdueInstallments,
        SUM(
            CASE
                WHEN (i.PrincipalAmount - i.PaidPrincipal) > 0 THEN (i.PrincipalAmount - i.PaidPrincipal)
                ELSE 0
            END
        )
        +
        SUM(
            CASE
                WHEN (i.InterestAmount - i.PaidInterest) > 0 THEN (i.InterestAmount - i.PaidInterest)
                ELSE 0
            END
        ) AS OverdueAmountNoPenalty,
        SUM(
            CASE
                WHEN (i.InterestAmount - i.PaidInterest) > 0 THEN (i.InterestAmount - i.PaidInterest)
                ELSE 0
            END
        ) AS OverdueInterestNoPenalty,
        ISNULL((
            SELECT TOP 1 (x.InterestAmount - x.PaidInterest)
            FROM dbo.LoanInstallments x
            WHERE x.LoanId = l.LoanId
              AND x.DueDate < @asOfDate
              AND x.Status <> 3
            ORDER BY x.DueDate ASC
        ), 0) AS FirstOverdueInterest,
        STUFF((
            SELECT CHAR(10) + CONVERT(varchar(10), CONVERT(date, x.DueDate), 23)
            FROM dbo.LoanInstallments x
            WHERE x.LoanId = l.LoanId
              AND x.DueDate < @asOfDate
              AND x.Status <> 3
            ORDER BY x.DueDate ASC
            FOR XML PATH(''), TYPE
        ).value('.', 'nvarchar(max)'), 1, 1, '') AS OverdueDueDatesText
    FROM dbo.LoanInstallments i
    WHERE i.LoanId = l.LoanId
      AND i.DueDate < @asOfDate
      AND i.Status <> 3
) overdueAgg

ORDER BY l.LoanId ASC;";

            if (_db.State != ConnectionState.Open)
                _db.Open();

            var rows = (await _db.QueryAsync<LoanSummaryReportRow>(sql, new
            {
                asOfDate = asOfDate.Date
            })).ToList();

            return rows;
        }
    }
}
