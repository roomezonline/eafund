using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public sealed class LoanInstallmentReminderRepository
    {
        private readonly IDbConnection _db;

        public LoanInstallmentReminderRepository(IDbConnection db)
        {
            _db = db;
        }

        public sealed class ReminderRow
        {
            public long LoanId { get; set; }
            public long InstallmentId { get; set; }
            public int InstallmentNo { get; set; }
            public DateTime DueDate { get; set; }

            public decimal EmiAmount { get; set; }
            public decimal InterestAmount { get; set; }
            public decimal PrincipalAmount { get; set; }

            public long MemberId { get; set; }
            public string? MemberCode { get; set; }
            public string MemberFullName { get; set; } = string.Empty;
            public string? MobileNumber { get; set; }

            public int DaysToDue { get; set; }
            public int OverdueDays { get; set; }
        }

        public async Task<IReadOnlyList<ReminderRow>> SearchAsync(
            int daysBeforeDue,
            bool includeOverdue,
            string? query,
            int take = 200)
        {
            if (_db.State != ConnectionState.Open)
                _db.Open();

            var sql = @"
SELECT TOP (@take)
    l.LoanId,
    i.InstallmentId,
    i.InstallmentNo,
    i.DueDate,
    i.EmiAmount,
    i.InterestAmount,
    i.PrincipalAmount,

    m.MemberId,
    l.MemberCode,
    LTRIM(RTRIM(CONCAT(ISNULL(m.FirstName, N''), N' ', ISNULL(m.LastName, N'')))) AS MemberFullName,
    m.MobileNumber,

    DATEDIFF(day, CAST(@today AS date), CAST(i.DueDate AS date)) AS DaysToDue,
    CASE WHEN CAST(i.DueDate AS date) < CAST(@today AS date) THEN DATEDIFF(day, CAST(i.DueDate AS date), CAST(@today AS date)) ELSE 0 END AS OverdueDays

FROM dbo.LoanInstallments i
INNER JOIN dbo.Loans l ON l.LoanId = i.LoanId
INNER JOIN dbo.Members m ON m.MemberId = l.MemberId
WHERE 1=1
  AND i.Status <> 3
  AND (
        (CAST(i.DueDate AS date) = DATEADD(day, @daysBeforeDue, CAST(@today AS date)))
        OR (@includeOverdue = 1 AND CAST(i.DueDate AS date) < CAST(@today AS date))
      )
";

            if (!string.IsNullOrWhiteSpace(query))
            {
                sql += @"
  AND (
        l.MemberCode LIKE @qLike
        OR m.MobileNumber LIKE @qLike
        OR LTRIM(RTRIM(CONCAT(ISNULL(m.FirstName, N''), N' ', ISNULL(m.LastName, N'')))) LIKE @qLike
        OR CAST(l.LoanId AS nvarchar(50)) LIKE @qDigitsLike
        OR CAST(i.InstallmentId AS nvarchar(50)) LIKE @qDigitsLike
      )
";
            }

            sql += " ORDER BY i.DueDate ASC, l.LoanId ASC, i.InstallmentNo ASC";

            var q = (query ?? string.Empty).Trim();
            var digits = new string(q.Where(char.IsDigit).ToArray());

            var rows = await _db.QueryAsync<ReminderRow>(sql, new
            {
                take,
                today = DateTime.Today,
                daysBeforeDue,
                includeOverdue = includeOverdue ? 1 : 0,
                qLike = "%" + q + "%",
                qDigitsLike = "%" + digits + "%"
            });

            return rows.ToList();
        }

        public async Task<int> CountOverdueBorrowersAsync()
        {
            if (_db.State != ConnectionState.Open)
                _db.Open();

            const string sql = @"
SELECT COUNT(DISTINCT l.MemberId)
FROM dbo.LoanInstallments i
INNER JOIN dbo.Loans l ON l.LoanId = i.LoanId
WHERE i.Status <> 3
  AND CAST(i.DueDate AS date) < CAST(@today AS date);";

            return await _db.QuerySingleAsync<int>(sql, new { today = DateTime.Today });
        }
    }
}
