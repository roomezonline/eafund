using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class BankAccountRepository
    {
        protected readonly IDbConnection Connection;

        public BankAccountRepository(IDbConnection connection)
        {
            Connection = connection;
        }

        public async Task<IEnumerable<BankAccount>> GetAllAsync(string? searchTerm = null, bool? isActive = null, int limit = 200)
        {
            var sql = @"
                SELECT *
                FROM BankAccounts
                WHERE 1=1";

            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                sql += " AND (BankName LIKE @q OR AccountNumber LIKE @q OR CardNumber LIKE @q OR Shaba LIKE @q OR AccountType LIKE @q)";
                parameters.Add("q", $"%{searchTerm}%");
            }

            if (isActive.HasValue)
            {
                sql += " AND IsActive = @isActive";
                parameters.Add("isActive", isActive.Value);
            }

            sql += " ORDER BY CreatedAt DESC";
            if (limit > 0)
            {
                sql += $" OFFSET 0 ROWS FETCH NEXT {limit} ROWS ONLY";
            }

            return await Connection.QueryAsync<BankAccount>(sql, parameters);
        }

        public async Task<BankAccount?> GetByIdAsync(long id)
        {
            var sql = "SELECT * FROM BankAccounts WHERE AccountId = @id";
            return await Connection.QueryFirstOrDefaultAsync<BankAccount>(sql, new { id });
        }

        public async Task<long> CreateAsync(BankAccount model)
        {
            var sql = @"
                INSERT INTO BankAccounts (BankName, AccountNumber, CardNumber, Shaba, AccountType, InterestRate, GlAccountId, IsActive, OpenedAt, CreatedAt)
                OUTPUT INSERTED.AccountId
                VALUES (@BankName, @AccountNumber, @CardNumber, @Shaba, @AccountType, @InterestRate, @GlAccountId, @IsActive, @OpenedAt, @CreatedAt)";
            var id = await Connection.QuerySingleAsync<long>(sql, new
            {
                model.BankName,
                model.AccountNumber,
                model.CardNumber,
                model.Shaba,
                model.AccountType,
                model.InterestRate,
                model.GlAccountId,
                model.IsActive,
                model.OpenedAt,
                CreatedAt = DateTime.UtcNow
            });
            return id;
        }

        public async Task UpdateAsync(BankAccount model)
        {
            var sql = @"
                UPDATE BankAccounts
                SET BankName = @BankName,
                    AccountNumber = @AccountNumber,
                    CardNumber = @CardNumber,
                    Shaba = @Shaba,
                    AccountType = @AccountType,
                    InterestRate = @InterestRate,
                    GlAccountId = @GlAccountId,
                    IsActive = @IsActive,
                    OpenedAt = @OpenedAt
                WHERE AccountId = @AccountId";
            await Connection.ExecuteAsync(sql, model);
        }

        public async Task DeleteAsync(long id)
        {
            var sql = "DELETE FROM BankAccounts WHERE AccountId = @id";
            await Connection.ExecuteAsync(sql, new { id });
        }
    }
}
