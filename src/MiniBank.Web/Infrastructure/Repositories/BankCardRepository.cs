using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class BankCardRepository
    {
        protected readonly IDbConnection Connection;

        public BankCardRepository(IDbConnection connection)
        {
            Connection = connection;
        }

        public async Task<IReadOnlyList<BankCard>> GetAllAsync(bool? isActive = null, int limit = 200)
        {
            var sql = @"
                SELECT *
                FROM BankCards
                WHERE 1=1";

            var parameters = new DynamicParameters();

            if (isActive.HasValue)
            {
                sql += " AND IsActive = @isActive";
                parameters.Add("isActive", isActive.Value);
            }

            sql += " ORDER BY IsActive DESC, CreatedAt DESC";
            if (limit > 0)
            {
                sql += $" OFFSET 0 ROWS FETCH NEXT {limit} ROWS ONLY";
            }

            var rows = await Connection.QueryAsync<BankCard>(sql, parameters);
            return rows.ToList();
        }

        public async Task<BankCard?> GetByIdAsync(long id)
        {
            const string sql = "SELECT * FROM BankCards WHERE CardId = @id";
            return await Connection.QueryFirstOrDefaultAsync<BankCard>(sql, new { id });
        }

        public async Task<long> CreateAsync(BankCard model)
        {
            const string sql = @"
                INSERT INTO BankCards (BankName, CardNumber, AccountNumber, Shaba, IsActive, CreatedAt)
                OUTPUT INSERTED.CardId
                VALUES (@BankName, @CardNumber, @AccountNumber, @Shaba, @IsActive, @CreatedAt)";

            return await Connection.QuerySingleAsync<long>(sql, new
            {
                model.BankName,
                model.CardNumber,
                model.AccountNumber,
                model.Shaba,
                model.IsActive,
                CreatedAt = DateTime.UtcNow
            });
        }

        public async Task UpdateAsync(BankCard model)
        {
            const string sql = @"
                UPDATE BankCards
                SET BankName = @BankName,
                    CardNumber = @CardNumber,
                    AccountNumber = @AccountNumber,
                    Shaba = @Shaba,
                    IsActive = @IsActive,
                    UpdatedAt = @UpdatedAt
                WHERE CardId = @CardId";

            await Connection.ExecuteAsync(sql, new
            {
                model.BankName,
                model.CardNumber,
                model.AccountNumber,
                model.Shaba,
                model.IsActive,
                UpdatedAt = DateTime.UtcNow,
                model.CardId
            });
        }

        public async Task DeleteAsync(long id)
        {
            const string sql = "DELETE FROM BankCards WHERE CardId = @id";
            await Connection.ExecuteAsync(sql, new { id });
        }

        public async Task SetActiveAsync(long id, bool isActive)
        {
            const string sql = "UPDATE BankCards SET IsActive = @isActive, UpdatedAt = @UpdatedAt WHERE CardId = @id";
            await Connection.ExecuteAsync(sql, new { id, isActive, UpdatedAt = DateTime.UtcNow });
        }
    }
}
