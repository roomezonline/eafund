using Dapper;
using MiniBank.Web.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class CreditHoldRepository
    {
        private readonly IDbConnection _connection;

        public sealed class CreditHoldDetail
        {
            public long HoldId { get; set; }
            public long LoanId { get; set; }
            public long MemberId { get; set; }
            public string? MemberCode { get; set; }
            public CreditHoldRole Role { get; set; }
            public decimal LockedAmount { get; set; }
            public string? Notes { get; set; }
            public DateTime CreatedAt { get; set; }
            public long BorrowerMemberId { get; set; }
            public string? BorrowerName { get; set; }
        }

        public CreditHoldRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public Task<IEnumerable<CreditHold>> GetActiveByMemberCodeAsync(string memberCode, IDbTransaction? tx = null)
        {
            const string sql = @"SELECT *
FROM dbo.CreditHolds
WHERE MemberCode = @memberCode AND Status = 1
ORDER BY HoldId DESC";
            return _connection.QueryAsync<CreditHold>(sql, new { memberCode }, tx);
        }

        public Task<IEnumerable<CreditHoldDetail>> GetActiveDetailsByMemberCodeAsync(string memberCode, IDbTransaction? tx = null)
        {
            const string sql = @"SELECT
    ch.HoldId,
    ch.LoanId,
    ch.MemberId,
    ch.MemberCode,
    ch.Role,
    ch.LockedAmount,
    ch.Notes,
    ch.CreatedAt,
    l.MemberId AS BorrowerMemberId,
    (m.FirstName + N' ' + m.LastName) AS BorrowerName
FROM dbo.CreditHolds ch
LEFT JOIN dbo.Loans l ON l.LoanId = ch.LoanId
LEFT JOIN dbo.Members m ON m.MemberId = l.MemberId
WHERE ch.MemberCode = @memberCode AND ch.Status = 1
ORDER BY ch.HoldId DESC";
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            return _connection.QueryAsync<CreditHoldDetail>(sql, new { memberCode }, tx);
        }

        public Task<decimal> SumActiveLockedAmountByMemberCodeAsync(string memberCode, IDbTransaction? tx = null)
        {
            const string sql = @"SELECT ISNULL(SUM(LockedAmount),0)
FROM dbo.CreditHolds
WHERE MemberCode = @memberCode AND Status = 1";
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            return _connection.QuerySingleAsync<decimal>(sql, new { memberCode }, tx);
        }

        public async Task<IReadOnlyDictionary<string, decimal>> SumActiveLockedAmountByMemberCodesAsync(IEnumerable<string> memberCodes, IDbTransaction? tx = null)
        {
            if (memberCodes == null) return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            var codes = memberCodes
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codes.Count == 0)
                return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            const string sql = @"SELECT MemberCode, ISNULL(SUM(LockedAmount),0) AS LockedAmount
FROM dbo.CreditHolds
WHERE Status = 1 AND MemberCode IN @codes
GROUP BY MemberCode;";

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            var rows = (await _connection.QueryAsync<(string MemberCode, decimal LockedAmount)>(sql, new { codes }, tx)).ToList();
            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.MemberCode)) continue;
                dict[r.MemberCode] = r.LockedAmount;
            }
            return dict;
        }

        public Task<IEnumerable<CreditHold>> GetByLoanIdAsync(long loanId, IDbTransaction? tx = null)
        {
            const string sql = @"SELECT *
FROM dbo.CreditHolds
WHERE LoanId = @loanId
ORDER BY HoldId ASC";
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            return _connection.QueryAsync<CreditHold>(sql, new { loanId }, tx);
        }

        public async Task<long> InsertAsync(CreditHold hold, IDbTransaction? tx = null)
        {
            const string sql = @"INSERT INTO dbo.CreditHolds (
    LoanId, MemberId, MemberCode,
    Role, LockPercent, BaseCapitalAmount, LockedAmount,
    Status, Notes, CreatedAt, ReleasedAt
)
OUTPUT INSERTED.HoldId
VALUES (
    @LoanId, @MemberId, @MemberCode,
    @Role, @LockPercent, @BaseCapitalAmount, @LockedAmount,
    @Status, @Notes, GETUTCDATE(), NULL
);";

            return await _connection.QuerySingleAsync<long>(sql, new
            {
                hold.LoanId,
                hold.MemberId,
                hold.MemberCode,
                Role = (byte)hold.Role,
                hold.LockPercent,
                hold.BaseCapitalAmount,
                hold.LockedAmount,
                Status = (byte)hold.Status,
                hold.Notes
            }, tx);
        }

        public Task<int> CancelByLoanIdAsync(long loanId, IDbTransaction? tx = null)
        {
            const string sql = @"UPDATE dbo.CreditHolds
SET Status = 3,
    ReleasedAt = GETUTCDATE()
WHERE LoanId = @loanId AND Status = 1;";
            return _connection.ExecuteAsync(sql, new { loanId }, tx);
        }

        public Task<int> ReleaseByLoanIdAsync(long loanId, IDbTransaction? tx = null)
        {
            const string sql = @"UPDATE dbo.CreditHolds
SET Status = 2,
    ReleasedAt = GETUTCDATE()
WHERE LoanId = @loanId AND Status = 1;";
            return _connection.ExecuteAsync(sql, new { loanId }, tx);
        }

        public Task<int> DeleteByLoanIdAsync(long loanId, IDbTransaction? tx = null)
        {
            const string sql = "DELETE FROM dbo.CreditHolds WHERE LoanId = @loanId";
            return _connection.ExecuteAsync(sql, new { loanId }, tx);
        }
    }
}
