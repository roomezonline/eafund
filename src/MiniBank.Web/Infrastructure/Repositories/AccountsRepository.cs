using Dapper;
using System.Data;
using System.Data.Common;
using MiniBank.Web.Models;
using Serilog;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class AccountsRepository
    {
        private readonly IDbConnection _db;
        public AccountsRepository(IDbConnection db) { _db = db; }

        private async Task<(long AccountId, bool IsActive)?> GetAccountIdByCodeAsync(string code)
        {
            await EnsureOpenAsync();
            const string sql = "SELECT TOP 1 AccountId, IsActive FROM dbo.Accounts WHERE Code = @code";
            return await _db.QuerySingleOrDefaultAsync<(long AccountId, bool IsActive)?>(sql, new { code });
        }

        public async Task<IEnumerable<(long AccountId, string Code, string Name, byte AccountType)>> GetAllAsync()
        {
            await EnsureOpenAsync();
            // Check if Level column exists to maintain compatibility during migration
            var hasLevelColumn = await HasLevelColumnAsync();
            var sql = hasLevelColumn 
                ? "SELECT AccountId, Code, Name, AccountType FROM dbo.Accounts WHERE IsActive = 1 ORDER BY Code"
                : "SELECT AccountId, Code, Name, AccountType FROM dbo.Accounts WHERE IsActive = 1 ORDER BY Code";
            return await _db.QueryAsync<(long, string, string, byte)>(sql);
        }

        public async Task<IEnumerable<Account>> GetAllAccountsAsync()
        {
            await EnsureOpenAsync();
            // Check if Level column exists to maintain compatibility during migration
            var hasLevelColumn = await HasLevelColumnAsync();
            var sql = hasLevelColumn 
                ? "SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt, Level FROM dbo.Accounts WHERE IsActive = 1 ORDER BY Code"
                : "SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt FROM dbo.Accounts WHERE IsActive = 1 ORDER BY Code";
            return await _db.QueryAsync<Account>(sql);
        }

        public async Task<IEnumerable<(long AccountId, string Code, string Name, byte AccountType)>> SearchAsync(string? q, int? accountType)
        {
            await EnsureOpenAsync();
            var where = "WHERE IsActive = 1";
            if (!string.IsNullOrWhiteSpace(q)) where += " AND (Code LIKE @pat OR Name LIKE @pat)";
            if (accountType.HasValue) where += " AND AccountType = @accountType";
            var sql = $"SELECT AccountId, Code, Name, AccountType FROM dbo.Accounts {where} ORDER BY Code";
            return await _db.QueryAsync<(long, string, string, byte)>(sql, new { pat = $"%{q}%", accountType });
        }

        public async Task<IEnumerable<Account>> SearchAsync(string? q, byte? level, bool activeOnly = true)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();
            var where = activeOnly ? "WHERE IsActive = 1" : "WHERE 1=1";
            
            if (!string.IsNullOrWhiteSpace(q)) where += " AND (Code LIKE @pat OR Name LIKE @pat)";
            if (level.HasValue && hasLevelColumn) where += " AND Level = @level";
            
            var sql = hasLevelColumn 
                ? $"SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt, Level FROM dbo.Accounts {where} ORDER BY Code"
                : $"SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt FROM dbo.Accounts {where} ORDER BY Code";
            
            return await _db.QueryAsync<Account>(sql, new { pat = $"%{q}%", level });
        }

        public async Task<IReadOnlyList<Account>> SearchMemberSubsidiariesAsync(string? q, bool activeOnly = true, int limit = 500)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();

            var where = activeOnly ? "WHERE IsActive = 1" : "WHERE 1=1";
            where += " AND MemberId IS NOT NULL AND Code LIKE 'M-%'";
            if (hasLevelColumn)
                where += " AND Level = 4";

            if (!string.IsNullOrWhiteSpace(q))
                where += " AND (Code LIKE @pat OR Name LIKE @pat)";

            var take = limit > 0 ? $"TOP ({limit})" : "";
            var sql = hasLevelColumn
                ? $"SELECT {take} AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt, Level FROM dbo.Accounts {where} ORDER BY Code"
                : $"SELECT {take} AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt FROM dbo.Accounts {where} ORDER BY Code";

            var rows = await _db.QueryAsync<Account>(sql, new { pat = $"%{q}%" });
            return rows.ToList();
        }

        public async Task<IEnumerable<Account>> GetAllWithHierarchyAsync()
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();
            var sql = hasLevelColumn 
                ? "SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt, Level FROM dbo.Accounts WHERE IsActive = 1 ORDER BY Level, Code"
                : "SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt FROM dbo.Accounts WHERE IsActive = 1 ORDER BY Code";
            
            var accounts = (await _db.QueryAsync<Account>(sql)).ToList();
            
            // Build hierarchy relationships
            var accountDict = accounts.ToDictionary(a => a.AccountId);
            foreach (var account in accounts)
            {
                if (account.ParentAccountId.HasValue && accountDict.ContainsKey(account.ParentAccountId.Value))
                {
                    account.Parent = accountDict[account.ParentAccountId.Value];
                    accountDict[account.ParentAccountId.Value].Children.Add(account);
                }
            }
            
            return accounts;
        }

        public async Task<long> CreateAsync(string code, string name, byte accountType)
        {
            await EnsureOpenAsync();

            var existing = await GetAccountIdByCodeAsync(code);
            if (existing.HasValue)
            {
                if (!existing.Value.IsActive)
                {
                    await UpdateAsync(existing.Value.AccountId, code, name, accountType, true);
                    return existing.Value.AccountId;
                }

                throw new InvalidOperationException("Account code already exists");
            }

            const string sql = @"
                DECLARE @ids TABLE (AccountId BIGINT);

                INSERT INTO dbo.Accounts (Code, Name, AccountType, IsActive, CreatedAt)
                OUTPUT INSERTED.AccountId INTO @ids(AccountId)
                VALUES (@code, @name, @accountType, 1, GETUTCDATE());

                SELECT TOP 1 AccountId FROM @ids;";
            
            return await _db.QuerySingleAsync<long>(sql, new { code, name, accountType });
        }

        public async Task<long> CreateAsync(Account account)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();

            var code = (account.Code ?? string.Empty).Trim();
            var existing = await GetAccountIdByCodeAsync(code);
            if (existing.HasValue)
            {
                if (!existing.Value.IsActive)
                {
                    account.Code = code;
                    account.IsActive = true;
                    await UpdateAsync(existing.Value.AccountId, account);
                    return existing.Value.AccountId;
                }

                throw new InvalidOperationException("Account code already exists");
            }

            try
            {
                var dbName = await _db.QuerySingleOrDefaultAsync<string>("SELECT DB_NAME()");
                Log.Information("AccountsRepository.CreateAsync(Account): inserting into DB={DbName}, Code={Code}, MemberId={MemberId}, Level={Level}, IsActive={IsActive}", dbName, code, account.MemberId, account.Level, account.IsActive);
            }
            catch
            {
            }

            var sql = hasLevelColumn 
                ? @"DECLARE @ids TABLE (AccountId BIGINT);

                    INSERT INTO dbo.Accounts (Code, Name, AccountType, ParentAccountId, MemberId, Level, IsActive, CreatedAt)
                    OUTPUT INSERTED.AccountId INTO @ids(AccountId)
                    VALUES (@Code, @Name, @AccountType, @ParentAccountId, @MemberId, @Level, @IsActive, GETUTCDATE());

                    SELECT TOP 1 AccountId FROM @ids;"
                : @"DECLARE @ids TABLE (AccountId BIGINT);

                    INSERT INTO dbo.Accounts (Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt)
                    OUTPUT INSERTED.AccountId INTO @ids(AccountId)
                    VALUES (@Code, @Name, @AccountType, @ParentAccountId, @MemberId, @IsActive, GETUTCDATE());

                    SELECT TOP 1 AccountId FROM @ids;";

            long newId;
            try
            {
                newId = await _db.QuerySingleAsync<long>(sql, account);
            }
            catch (Exception ex)
            {
                try
                {
                    Log.Error(ex, "AccountsRepository.CreateAsync(Account) failed for Code={Code}, MemberId={MemberId}", code, account.MemberId);
                }
                catch
                {
                }
                throw;
            }

            if (newId <= 0)
            {
                try
                {
                    Log.Error("AccountsRepository.CreateAsync(Account) returned invalid id={AccountId} for Code={Code}, MemberId={MemberId}", newId, code, account.MemberId);
                }
                catch
                {
                }
                throw new InvalidOperationException("Account insert failed: invalid AccountId returned");
            }

            try
            {
                Log.Information("AccountsRepository.CreateAsync(Account): insert succeeded AccountId={AccountId} Code={Code}", newId, code);
            }
            catch
            {
            }

            if (!hasLevelColumn && account.Level.HasValue)
            {
                try
                {
                    const string updateLevelSql = "UPDATE dbo.Accounts SET Level = @Level WHERE AccountId = @AccountId";
                    await _db.ExecuteAsync(updateLevelSql, new { AccountId = newId, Level = account.Level });
                }
                catch
                {
                }
            }

            return newId;
        }

        public async Task<bool> UpdateAsync(long accountId, string code, string name, byte accountType, bool isActive)
        {
            await EnsureOpenAsync();
            const string sql = @"
                UPDATE dbo.Accounts 
                SET Code = @code, Name = @name, AccountType = @accountType, IsActive = @isActive
                WHERE AccountId = @accountId";
            
            var rowsAffected = await _db.ExecuteAsync(sql, new { accountId, code, name, accountType, isActive });
            return rowsAffected > 0;
        }

        public async Task<bool> UpdateAsync(long accountId, Account account)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();
            var sql = hasLevelColumn 
                ? @"UPDATE dbo.Accounts 
                    SET Code = @Code, Name = @Name, AccountType = @AccountType, ParentAccountId = @ParentAccountId, MemberId = @MemberId, Level = @Level, IsActive = @IsActive
                    WHERE AccountId = @AccountId"
                : @"UPDATE dbo.Accounts 
                    SET Code = @Code, Name = @Name, AccountType = @AccountType, ParentAccountId = @ParentAccountId, MemberId = @MemberId, IsActive = @IsActive
                    WHERE AccountId = @AccountId";
            
            account.AccountId = accountId;
            var rowsAffected = await _db.ExecuteAsync(sql, account);

            if (!hasLevelColumn && account.Level.HasValue)
            {
                try
                {
                    const string updateLevelSql = "UPDATE dbo.Accounts SET Level = @Level WHERE AccountId = @AccountId";
                    await _db.ExecuteAsync(updateLevelSql, new { AccountId = accountId, Level = account.Level });
                }
                catch
                {
                }
            }

            return rowsAffected > 0;
        }

        public async Task<bool> DeleteAsync(long accountId)
        {
            await EnsureOpenAsync();
            const string sql = "UPDATE dbo.Accounts SET IsActive = 0 WHERE AccountId = @accountId";
            var rowsAffected = await _db.ExecuteAsync(sql, new { accountId });
            return rowsAffected > 0;
        }

        public async Task<bool> HardDeleteAsync(long accountId)
        {
            await EnsureOpenAsync();
            const string sql = "DELETE FROM dbo.Accounts WHERE AccountId = @accountId";
            var rowsAffected = await _db.ExecuteAsync(sql, new { accountId });
            return rowsAffected > 0;
        }

        public async Task<bool> HasJournalLinesAsync(long accountId)
        {
            await EnsureOpenAsync();
            const string sql = @"SELECT COUNT(1) FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                WHERE jl.AccountId = @accountId";
            var count = await _db.QuerySingleOrDefaultAsync<int>(sql, new { accountId });
            return count > 0;
        }

        public async Task<IReadOnlyList<Account>> GetAccountsByMemberIdAsync(long memberId, bool includeInactive = false)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();
            var where = includeInactive ? "WHERE MemberId = @memberId" : "WHERE MemberId = @memberId AND IsActive = 1";
            var sql = hasLevelColumn
                ? $"SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt, Level FROM dbo.Accounts {where} ORDER BY AccountId"
                : $"SELECT AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt FROM dbo.Accounts {where} ORDER BY AccountId";
            return (await _db.QueryAsync<Account>(sql, new { memberId })).ToList();
        }

        public async Task<(long AccountId, string Code, string Name, byte AccountType, bool IsActive)?> GetByIdAsync(long accountId)
        {
            await EnsureOpenAsync();
            const string sql = "SELECT AccountId, Code, Name, AccountType, IsActive FROM dbo.Accounts WHERE AccountId = @accountId";
            return await _db.QuerySingleOrDefaultAsync<(long AccountId, string Code, string Name, byte AccountType, bool IsActive)?>(sql, new { accountId });
        }

        public async Task<int?> GetMemberIdByAccountIdAsync(long accountId)
        {
            await EnsureOpenAsync();
            const string sql = "SELECT MemberId FROM dbo.Accounts WHERE AccountId = @accountId";
            return await _db.QuerySingleOrDefaultAsync<int?>(sql, new { accountId });
        }

        public async Task<bool> IsCodeUniqueAsync(string code, long? excludeAccountId = null)
        {
            await EnsureOpenAsync();
            var sql = "SELECT COUNT(1) FROM dbo.Accounts WHERE Code = @code";
            if (excludeAccountId.HasValue)
                sql += " AND AccountId != @excludeAccountId";
            
            var count = await _db.QuerySingleAsync<int>(sql, new { code, excludeAccountId });
            return count == 0;
        }

        public async Task<Account?> FindInactiveByCodeAsync(string code)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();
            var sql = hasLevelColumn 
                ? "SELECT AccountId, Code, Name, AccountType, ParentAccountId, IsActive, CreatedAt, Level FROM dbo.Accounts WHERE Code = @code AND IsActive = 0"
                : "SELECT AccountId, Code, Name, AccountType, ParentAccountId, IsActive, CreatedAt FROM dbo.Accounts WHERE Code = @code AND IsActive = 0";
            return await _db.QuerySingleOrDefaultAsync<Account>(sql, new { code });
        }

        public async Task ReactivateAsync(long accountId)
        {
            await EnsureOpenAsync();
            var sql = "UPDATE dbo.Accounts SET IsActive = 1 WHERE AccountId = @accountId";
            await _db.ExecuteAsync(sql, new { accountId });
        }

        private async Task<bool> HasLevelColumnAsync()
        {
            try
            {
                const string sql = "SELECT COL_LENGTH('dbo.Accounts', 'Level')";
                await EnsureOpenAsync();
                var result = await _db.QuerySingleOrDefaultAsync<int?>(sql);
                return result.HasValue;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if an account has child accounts (dependencies)
        /// </summary>
        public async Task<bool> HasChildAccountsAsync(long accountId)
        {
            await EnsureOpenAsync();
            const string sql = "SELECT COUNT(1) FROM dbo.Accounts WHERE ParentAccountId = @accountId AND IsActive = 1";
            var count = await _db.QuerySingleAsync<int>(sql, new { accountId });
            return count > 0;
        }

        /// <summary>
        /// Get child accounts for dependency checking
        /// </summary>
        public async Task<IEnumerable<(string Code, string Name, byte Level)>> GetChildAccountsAsync(long accountId)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();
            var sql = hasLevelColumn 
                ? "SELECT Code, Name, Level FROM dbo.Accounts WHERE ParentAccountId = @accountId AND IsActive = 1 ORDER BY Code"
                : "SELECT Code, Name, 0 as Level FROM dbo.Accounts WHERE ParentAccountId = @accountId AND IsActive = 1 ORDER BY Code";
            return await _db.QueryAsync<(string, string, byte)>(sql, new { accountId });
        }

        /// <summary>
        /// Check if account is used in journal entries
        /// </summary>
        public async Task<bool> IsUsedInJournalEntriesAsync(long accountId)
        {
            await EnsureOpenAsync();
            try
            {
                const string sql = @"SELECT COUNT(1) FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                WHERE jl.AccountId = @accountId";
                var count = await _db.QuerySingleAsync<int>(sql, new { accountId });
                return count > 0;
            }
            catch
            {
                // JournalLines table might not exist yet
                return false;
            }
        }

        /// <summary>
        /// Check if account is used in posting templates
        /// </summary>
        public async Task<bool> IsUsedInPostingTemplatesAsync(long accountId)
        {
            await EnsureOpenAsync();
            try
            {
                const string sql = "SELECT COUNT(1) FROM dbo.PostingTemplateLines WHERE AccountId = @accountId";
                var count = await _db.QuerySingleAsync<int>(sql, new { accountId });
                return count > 0;
            }
            catch
            {
                // PostingTemplateLines table might not exist yet
                return false;
            }
        }

        /// <summary>
        /// Get comprehensive dependency information for an account
        /// </summary>
        public async Task<AccountDependencyInfo> GetDependencyInfoAsync(long accountId)
        {
            var hasChildren = await HasChildAccountsAsync(accountId);
            var childAccounts = hasChildren ? await GetChildAccountsAsync(accountId) : Enumerable.Empty<(string, string, byte)>();
            var usedInJournals = await IsUsedInJournalEntriesAsync(accountId);
            var usedInTemplates = await IsUsedInPostingTemplatesAsync(accountId);

            return new AccountDependencyInfo
            {
                HasChildAccounts = hasChildren,
                ChildAccounts = childAccounts.ToList(),
                IsUsedInJournalEntries = usedInJournals,
                IsUsedInPostingTemplates = usedInTemplates,
                CanDelete = !hasChildren && !usedInJournals && !usedInTemplates
            };
        }

        public async Task<Account?> GetSubsidiaryByMemberIdAsync(long memberId, bool includeInactive = false)
        {
            await EnsureOpenAsync();
            var hasLevelColumn = await HasLevelColumnAsync();
            var activeFilter = includeInactive ? "" : " AND IsActive = 1";
            var sql = hasLevelColumn
                ? @"SELECT TOP 1 AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt, Level
                     FROM dbo.Accounts
                     WHERE MemberId = @memberId" + activeFilter + @"
                     ORDER BY IsActive DESC, AccountId"
                : @"SELECT TOP 1 AccountId, Code, Name, AccountType, ParentAccountId, MemberId, IsActive, CreatedAt
                     FROM dbo.Accounts
                     WHERE MemberId = @memberId" + activeFilter + @"
                     ORDER BY IsActive DESC, AccountId";

            return await _db.QuerySingleOrDefaultAsync<Account>(sql, new { memberId });
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

    public class AccountDependencyInfo
    {
        public bool HasChildAccounts { get; set; }
        public List<(string Code, string Name, byte Level)> ChildAccounts { get; set; } = new();
        public bool IsUsedInJournalEntries { get; set; }
        public bool IsUsedInPostingTemplates { get; set; }
        public bool CanDelete { get; set; }
        
        public string GetDependencyMessage()
        {
            var messages = new List<string>();
            
            if (HasChildAccounts)
            {
                var levelNames = new Dictionary<byte, string> { {1, "گروه"}, {2, "کل"}, {3, "معین"}, {4, "تفضیلی"} };
                var childTypes = ChildAccounts.GroupBy(c => c.Level)
                    .Select(g => $"{g.Count()} {levelNames.GetValueOrDefault(g.Key, "حساب")}")
                    .ToList();
                messages.Add($"دارای {string.Join(" و ", childTypes)} وابسته");
            }
            
            if (IsUsedInJournalEntries)
                messages.Add("در اسناد حسابداری استفاده شده");
                
            if (IsUsedInPostingTemplates)
                messages.Add("در الگوهای ثبت استفاده شده");
            
            return messages.Any() ? string.Join("، ", messages) : string.Empty;
        }
    }
}
