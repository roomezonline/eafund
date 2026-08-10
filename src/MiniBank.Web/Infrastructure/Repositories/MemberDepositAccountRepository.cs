using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class MemberDepositAccountRepository
    {
        private readonly IDbConnection _connection;
        public MemberDepositAccountRepository(IDbConnection connection) { _connection = connection; }

        public async Task<long> CreateAsync(MemberDepositAccount model)
        {
            const string sql = @"
INSERT INTO dbo.MemberDepositAccounts (MemberId, DepositTypeId, AccountNo, OpenedAt, ClosedAt, IsActive, Notes)
OUTPUT INSERTED.MemberDepositAccountId
VALUES (@MemberId, @DepositTypeId, @AccountNo, @OpenedAt, @ClosedAt, @IsActive, @Notes);";
            return await _connection.QuerySingleAsync<long>(sql, model);
        }

        public async Task<IEnumerable<MemberDepositAccount>> GetActiveByMemberAsync(long memberId)
        {
            const string sql = @"
SELECT mda.*, dt.Title AS DepositTypeTitle
FROM dbo.MemberDepositAccounts mda
JOIN dbo.DepositTypes dt ON dt.DepositTypeId = mda.DepositTypeId
WHERE mda.MemberId = @memberId AND mda.IsActive = 1
ORDER BY mda.OpenedAt DESC;";
            return await _connection.QueryAsync<MemberDepositAccount>(sql, new { memberId });
        }

        public async Task<bool> CloseAsync(long id, DateTime? closedAt = null)
        {
            const string sql = @"UPDATE dbo.MemberDepositAccounts SET IsActive = 0, ClosedAt = ISNULL(@closedAt, GETUTCDATE()) WHERE MemberDepositAccountId = @id";
            var rows = await _connection.ExecuteAsync(sql, new { id, closedAt });
            return rows > 0;
        }
    }
}
