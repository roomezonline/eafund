using Dapper;
using System.Data;
using System.Data.Common;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class AccountTypeRepository
    {
        private readonly IDbConnection _db;
        public AccountTypeRepository(IDbConnection db) { _db = db; }

        public async Task<IEnumerable<AccountType>> GetAllAsync(bool onlyActive = true)
        {
            await EnsureOpenAsync();
            var sql = "SELECT AccountTypeId, Code, Name, Description, IsActive, CreatedAt FROM dbo.AccountTypes" + 
                     (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY Code";
            return await _db.QueryAsync<AccountType>(sql);
        }

        public async Task<AccountType?> GetByIdAsync(int accountTypeId)
        {
            await EnsureOpenAsync();
            const string sql = "SELECT AccountTypeId, Code, Name, Description, IsActive, CreatedAt FROM dbo.AccountTypes WHERE AccountTypeId = @accountTypeId";
            return await _db.QuerySingleOrDefaultAsync<AccountType>(sql, new { accountTypeId });
        }

        public async Task<int> CreateAsync(string code, string name, string? description = null)
        {
            await EnsureOpenAsync();
            const string sql = @"
                INSERT INTO dbo.AccountTypes (Code, Name, Description, IsActive, CreatedAt)
                OUTPUT INSERTED.AccountTypeId
                VALUES (@code, @name, @description, 1, GETUTCDATE())";
            return await _db.QuerySingleAsync<int>(sql, new { code, name, description });
        }

        public async Task<bool> UpdateAsync(int accountTypeId, string code, string name, string? description, bool isActive)
        {
            await EnsureOpenAsync();
            const string sql = @"
                UPDATE dbo.AccountTypes 
                SET Code = @code, Name = @name, Description = @description, IsActive = @isActive
                WHERE AccountTypeId = @accountTypeId";
            var rows = await _db.ExecuteAsync(sql, new { accountTypeId, code, name, description, isActive });
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int accountTypeId)
        {
            await EnsureOpenAsync();
            const string sql = "UPDATE dbo.AccountTypes SET IsActive = 0 WHERE AccountTypeId = @accountTypeId";
            var rows = await _db.ExecuteAsync(sql, new { accountTypeId });
            return rows > 0;
        }

        public async Task<bool> IsCodeUniqueAsync(string code, int? excludeAccountTypeId = null)
        {
            await EnsureOpenAsync();
            var sql = "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountTypes WHERE Code = @code";
            if (excludeAccountTypeId.HasValue)
                sql += " AND AccountTypeId != @excludeAccountTypeId";
            sql += ") THEN 0 ELSE 1 END";
            
            var isUnique = await _db.ExecuteScalarAsync<int>(sql, new { code, excludeAccountTypeId });
            return isUnique == 1;
        }

        public async Task<bool> IsUsedInAccountsAsync(int accountTypeId)
        {
            await EnsureOpenAsync();
            const string sql = @"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Accounts WHERE AccountType = @accountTypeId) THEN 1 ELSE 0 END";
            var used = await _db.ExecuteScalarAsync<int>(sql, new { accountTypeId });
            return used == 1;
        }

        public async Task<int> CountUsagesAsync(int accountTypeId)
        {
            await EnsureOpenAsync();
            const string sql = @"SELECT COUNT(1) FROM dbo.Accounts WHERE AccountType = @accountTypeId";
            return await _db.ExecuteScalarAsync<int>(sql, new { accountTypeId });
        }

        private async Task EnsureOpenAsync()
        {
            if (_db.State != ConnectionState.Open)
            {
                if (_db is DbConnection dbc)
                    await dbc.OpenAsync();
                else
                    _db.Open();
            }
        }
    }
}
