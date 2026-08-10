using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class LoanRepository
    {
        private readonly IDbConnection _connection;

        public LoanRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public Task<Loan?> GetByIdAsync(long loanId)
        {
            const string sql = "SELECT * FROM dbo.Loans WHERE LoanId = @loanId";
            return _connection.QueryFirstOrDefaultAsync<Loan>(sql, new { loanId });
        }

        public Task<Loan?> GetByIdAsync(long loanId, IDbTransaction? tx)
        {
            const string sql = "SELECT * FROM dbo.Loans WHERE LoanId = @loanId";
            return _connection.QueryFirstOrDefaultAsync<Loan>(sql, new { loanId }, tx);
        }

        public Task<IEnumerable<Loan>> GetByMemberAsync(long memberId, LoanStatus? status = null)
        {
            var sql = "SELECT * FROM dbo.Loans WHERE MemberId = @memberId";
            if (status.HasValue)
                sql += " AND Status = @status";
            sql += " ORDER BY LoanId DESC";

            return _connection.QueryAsync<Loan>(sql, new { memberId, status });
        }

        public Task<IEnumerable<Loan>> GetByMemberCodeAsync(string memberCode, LoanStatus? status = null)
        {
            var sql = "SELECT * FROM dbo.Loans WHERE MemberCode = @memberCode";
            if (status.HasValue)
                sql += " AND Status = @status";
            sql += " ORDER BY LoanId DESC";

            return _connection.QueryAsync<Loan>(sql, new { memberCode, status });
        }

        public Task<IEnumerable<Loan>> GetAllAsync(LoanStatus? status = null, int limit = 500)
        {
            var sql = "SELECT TOP (@limit) * FROM dbo.Loans WHERE 1=1";
            if (status.HasValue)
                sql += " AND Status = @status";
            sql += " ORDER BY LoanId DESC";

            return _connection.QueryAsync<Loan>(sql, new { status, limit });
        }

        public Task<IEnumerable<Loan>> GetAllPagedAsync(int offset, int pageSize, LoanStatus? status = null)
        {
            var sql = @"SELECT *
FROM dbo.Loans
WHERE 1=1";
            if (status.HasValue)
                sql += " AND Status = @status";
            sql += " ORDER BY LoanId DESC OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            return _connection.QueryAsync<Loan>(sql, new { offset, pageSize, status });
        }

        public Task<Loan?> GetLatestActiveByMemberCodeAsync(string memberCode, IDbTransaction? tx = null)
        {
            const string sql = @"SELECT TOP 1 *
FROM dbo.Loans
WHERE MemberCode = @memberCode
  AND Status = 3
ORDER BY LoanId DESC";
            return _connection.QueryFirstOrDefaultAsync<Loan>(sql, new { memberCode }, tx);
        }

        public Task<long> CreateAsync(Loan model, IDbTransaction? tx = null)
        {
            const string sql = @"INSERT INTO dbo.Loans (
    MemberId, MemberCode, LoanRatePlanId, Principal, TermMonths,
    DisbursedAt, FirstDueDate,
    AnnualRatePercent, PenaltyAnnualRatePercent,
    EmiAmount, OutstandingPrincipal,
    Status, Notes, CreatedAt, UpdatedAt
)
OUTPUT INSERTED.LoanId
VALUES (
    @MemberId, @MemberCode, @LoanRatePlanId, @Principal, @TermMonths,
    @DisbursedAt, @FirstDueDate,
    @AnnualRatePercent, @PenaltyAnnualRatePercent,
    @EmiAmount, @OutstandingPrincipal,
    @Status, @Notes, GETUTCDATE(), NULL
);";

            return _connection.QuerySingleAsync<long>(sql, new
            {
                model.MemberId,
                model.MemberCode,
                model.LoanRatePlanId,
                model.Principal,
                model.TermMonths,
                DisbursedAt = model.DisbursedAt.Date,
                FirstDueDate = model.FirstDueDate.Date,
                model.AnnualRatePercent,
                model.PenaltyAnnualRatePercent,
                model.EmiAmount,
                model.OutstandingPrincipal,
                Status = (byte)model.Status,
                model.Notes
            }, tx);
        }

        public Task UpdateAsync(Loan model, IDbTransaction? tx = null)
        {
            const string sql = @"UPDATE dbo.Loans
SET MemberId = @MemberId,
    MemberCode = @MemberCode,
    LoanRatePlanId = @LoanRatePlanId,
    Principal = @Principal,
    TermMonths = @TermMonths,
    DisbursedAt = @DisbursedAt,
    FirstDueDate = @FirstDueDate,
    AnnualRatePercent = @AnnualRatePercent,
    PenaltyAnnualRatePercent = @PenaltyAnnualRatePercent,
    EmiAmount = @EmiAmount,
    OutstandingPrincipal = @OutstandingPrincipal,
    Status = @Status,
    Notes = @Notes,
    UpdatedAt = GETUTCDATE()
WHERE LoanId = @LoanId;";

            return _connection.ExecuteAsync(sql, new
            {
                model.LoanId,
                model.MemberId,
                model.MemberCode,
                model.LoanRatePlanId,
                model.Principal,
                model.TermMonths,
                DisbursedAt = model.DisbursedAt.Date,
                FirstDueDate = model.FirstDueDate.Date,
                model.AnnualRatePercent,
                model.PenaltyAnnualRatePercent,
                model.EmiAmount,
                model.OutstandingPrincipal,
                Status = (byte)model.Status,
                model.Notes
            }, tx);
        }

        public Task<int> UpdateStatusAsync(long loanId, LoanStatus status, IDbTransaction? tx = null)
        {
            const string sql = "UPDATE dbo.Loans SET Status = @status, UpdatedAt = GETUTCDATE() WHERE LoanId = @loanId";
            return _connection.ExecuteAsync(sql, new { loanId, status = (byte)status }, tx);
        }

        public Task<int> DeleteAsync(long loanId, IDbTransaction? tx = null)
        {
            const string sql = "DELETE FROM dbo.Loans WHERE LoanId = @loanId";
            return _connection.ExecuteAsync(sql, new { loanId }, tx);
        }
    }
}
