using Dapper;
using MiniBank.Web.Models.Auth;
using System.Data;
using System.Data.Common;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class AuthRepository
    {
        private readonly IDbConnection _db;

        public AuthRepository(IDbConnection db)
        {
            _db = db;
        }

        private async Task EnsureOpenAsync()
        {
            if (_db.State == ConnectionState.Open)
                return;

            if (_db is DbConnection dbc)
                await dbc.OpenAsync();
            else
                _db.Open();
        }

        public async Task<AuthUser?> GetUserByUsernameAsync(string username)
        {
            await EnsureOpenAsync();
            const string sql = @"
SELECT u.*, m.FirstName AS MemberFirstName, m.LastName AS MemberLastName, r.RoleId, r.Name, r.IsSystem, r.IsActive, r.CreatedAt
FROM dbo.AuthUsers u
LEFT JOIN dbo.Members m ON m.MemberId = u.MemberId
INNER JOIN dbo.AuthRoles r ON r.RoleId = u.RoleId
WHERE u.Username = @username";

            var rows = await _db.QueryAsync<AuthUser, AuthRole, AuthUser>(
                sql,
                (u, r) => { u.Role = r; return u; },
                new { username },
                splitOn: "RoleId");

            return rows.FirstOrDefault();
        }

        public async Task<AuthUser?> GetUserByIdAsync(long userId)
        {
            await EnsureOpenAsync();
            const string sql = @"
SELECT u.*, m.FirstName AS MemberFirstName, m.LastName AS MemberLastName, r.RoleId, r.Name, r.IsSystem, r.IsActive, r.CreatedAt
FROM dbo.AuthUsers u
LEFT JOIN dbo.Members m ON m.MemberId = u.MemberId
INNER JOIN dbo.AuthRoles r ON r.RoleId = u.RoleId
WHERE u.UserId = @userId";

            var rows = await _db.QueryAsync<AuthUser, AuthRole, AuthUser>(
                sql,
                (u, r) => { u.Role = r; return u; },
                new { userId },
                splitOn: "RoleId");

            return rows.FirstOrDefault();
        }

        public async Task<AuthRole?> GetRoleByNameAsync(string name)
        {
            await EnsureOpenAsync();
            return await _db.QueryFirstOrDefaultAsync<AuthRole>(
                "SELECT TOP 1 * FROM dbo.AuthRoles WHERE Name = @name", new { name });
        }

        public async Task<IReadOnlyList<AuthRole>> GetRolesAsync(bool includeInactive = true)
        {
            await EnsureOpenAsync();
            var sql = "SELECT * FROM dbo.AuthRoles" + (includeInactive ? "" : " WHERE IsActive = 1") + " ORDER BY IsSystem DESC, Name ASC";
            var rows = await _db.QueryAsync<AuthRole>(sql);
            return rows.ToList();
        }

        public async Task<long> CreateRoleAsync(string name, bool isSystem = false, bool isActive = true)
        {
            await EnsureOpenAsync();
            const string sql = @"
INSERT INTO dbo.AuthRoles([Name],[IsSystem],[IsActive])
OUTPUT INSERTED.RoleId
VALUES (@name,@isSystem,@isActive)";
            return await _db.QuerySingleAsync<long>(sql, new { name, isSystem, isActive });
        }

        public async Task UpdateRoleAsync(long roleId, string name, bool isActive)
        {
            await EnsureOpenAsync();
            const string sql = "UPDATE dbo.AuthRoles SET Name=@name, IsActive=@isActive WHERE RoleId=@roleId";
            await _db.ExecuteAsync(sql, new { roleId, name, isActive });
        }

        public async Task DeleteRoleAsync(long roleId)
        {
            await EnsureOpenAsync();
            await _db.ExecuteAsync("DELETE FROM dbo.AuthRoles WHERE RoleId=@roleId", new { roleId });
        }

        public async Task<bool> CanDeleteRoleAsync(long roleId)
        {
            await EnsureOpenAsync();
            var cnt = await _db.QuerySingleAsync<int>("SELECT COUNT(1) FROM dbo.AuthUsers WHERE RoleId=@roleId", new { roleId });
            return cnt <= 0;
        }

        public async Task DeleteRoleSafeAsync(long roleId)
        {
            await EnsureOpenAsync();
            const string sql = @"
DELETE FROM dbo.AuthRolePagePermissions WHERE RoleId=@roleId;
DELETE FROM dbo.AuthRoles WHERE RoleId=@roleId;";
            await _db.ExecuteAsync(sql, new { roleId });
        }

        public async Task<IReadOnlyList<AuthPage>> GetPagesAsync(bool includeInactive = true)
        {
            await EnsureOpenAsync();
            var sql = "SELECT * FROM dbo.AuthPages" + (includeInactive ? "" : " WHERE IsActive = 1") + " ORDER BY Title ASC";
            var rows = await _db.QueryAsync<AuthPage>(sql);
            return rows.ToList();
        }

        public async Task<AuthPage?> GetPageByKeyAsync(string pageKey)
        {
            await EnsureOpenAsync();
            return await _db.QueryFirstOrDefaultAsync<AuthPage>(
                "SELECT TOP 1 * FROM dbo.AuthPages WHERE PageKey = @pageKey", new { pageKey });
        }

        public async Task<long> UpsertPageAsync(string pageKey, string title, string path, bool isActive = true)
        {
            await EnsureOpenAsync();
            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.AuthPages WHERE PageKey = @pageKey)
BEGIN
    UPDATE dbo.AuthPages SET Title=@title, Path=@path, IsActive=@isActive WHERE PageKey=@pageKey;
    SELECT CAST((SELECT TOP 1 PageId FROM dbo.AuthPages WHERE PageKey=@pageKey) AS BIGINT);
END
ELSE
BEGIN
    INSERT INTO dbo.AuthPages([PageKey],[Title],[Path],[IsActive])
    OUTPUT INSERTED.PageId
    VALUES (@pageKey,@title,@path,@isActive);
END";
            return await _db.QuerySingleAsync<long>(sql, new { pageKey, title, path, isActive });
        }

        public async Task UpdatePageAsync(long pageId, string title, string path, bool isActive)
        {
            await EnsureOpenAsync();
            const string sql = "UPDATE dbo.AuthPages SET Title=@title, Path=@path, IsActive=@isActive WHERE PageId=@pageId";
            await _db.ExecuteAsync(sql, new { pageId, title, path, isActive });
        }

        public async Task<IReadOnlyList<(long PageId, bool Allowed)>> GetRolePagePermissionsAsync(long roleId)
        {
            await EnsureOpenAsync();
            const string sql = @"
SELECT p.PageId, ISNULL(rp.Allowed, 0) AS Allowed
FROM dbo.AuthPages p
LEFT JOIN dbo.AuthRolePagePermissions rp ON rp.PageId = p.PageId AND rp.RoleId = @roleId";
            var rows = await _db.QueryAsync<(long PageId, bool Allowed)>(sql, new { roleId });
            return rows.ToList();
        }

        public async Task SetRolePagePermissionAsync(long roleId, long pageId, bool allowed)
        {
            await EnsureOpenAsync();
            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.AuthRolePagePermissions WHERE RoleId=@roleId AND PageId=@pageId)
BEGIN
    UPDATE dbo.AuthRolePagePermissions SET Allowed=@allowed, UpdatedAt=GETUTCDATE() WHERE RoleId=@roleId AND PageId=@pageId;
END
ELSE
BEGIN
    INSERT INTO dbo.AuthRolePagePermissions(RoleId, PageId, Allowed) VALUES (@roleId, @pageId, @allowed);
END";
            await _db.ExecuteAsync(sql, new { roleId, pageId, allowed });
        }

        public async Task SetRoleAllPagePermissionsAsync(long roleId, bool allowed, bool activePagesOnly = true)
        {
            await EnsureOpenAsync();

            if (!allowed)
            {
                await _db.ExecuteAsync("DELETE FROM dbo.AuthRolePagePermissions WHERE RoleId=@roleId", new { roleId });
                return;
            }

            var sqlPages = "SELECT PageId FROM dbo.AuthPages" + (activePagesOnly ? " WHERE IsActive = 1" : string.Empty);
            var pageIds = (await _db.QueryAsync<long>(sqlPages)).ToList();

            await _db.ExecuteAsync("DELETE FROM dbo.AuthRolePagePermissions WHERE RoleId=@roleId", new { roleId });

            const string insertSql = @"
INSERT INTO dbo.AuthRolePagePermissions(RoleId, PageId, Allowed, UpdatedAt)
VALUES (@roleId, @pageId, 1, GETUTCDATE());";

            foreach (var pageId in pageIds)
            {
                await _db.ExecuteAsync(insertSql, new { roleId, pageId });
            }
        }

        public async Task<IReadOnlyList<AuthUser>> GetUsersAsync(string? keyword = null, long? roleId = null, string? sortBy = null, int top = 500)
        {
            await EnsureOpenAsync();
            var sql = @"
SELECT TOP (@top)
    u.*, m.FirstName AS MemberFirstName, m.LastName AS MemberLastName, r.RoleId, r.Name, r.IsSystem, r.IsActive, r.CreatedAt
FROM dbo.AuthUsers u
LEFT JOIN dbo.Members m ON m.MemberId = u.MemberId
INNER JOIN dbo.AuthRoles r ON r.RoleId = u.RoleId
WHERE 1=1";

            var p = new DynamicParameters(new { top });

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += @" AND (
    u.Username LIKE @kw
    OR r.Name LIKE @kw
    OR m.FirstName LIKE @kw
    OR m.LastName LIKE @kw
    OR m.NationalCode LIKE @kw
    OR CAST(m.MemberId AS NVARCHAR(32)) LIKE @kw
)";
                p.Add("kw", $"%{keyword.Trim()}%");
            }

            if (roleId.HasValue)
            {
                sql += " AND u.RoleId = @roleId";
                p.Add("roleId", roleId.Value);
            }

            sortBy = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
            if (sortBy == "role")
            {
                sql += @" ORDER BY
CASE r.Name
    WHEN 'Developer' THEN 1
    WHEN 'Admin' THEN 2
    WHEN 'Accountant' THEN 3
    WHEN 'Manager' THEN 4
    WHEN 'Member' THEN 5
    ELSE 99
END,
u.UserId DESC";
            }
            else
            {
                sql += " ORDER BY u.UserId DESC";
            }

            var rows = await _db.QueryAsync<AuthUser, AuthRole, AuthUser>(
                sql,
                (u, r) => { u.Role = r; return u; },
                p,
                splitOn: "RoleId");

            return rows.ToList();
        }

        public async Task<long> CreateUserAsync(AuthUser user)
        {
            await EnsureOpenAsync();
            const string sql = @"
INSERT INTO dbo.AuthUsers(Username, PasswordEnc, RoleId, MemberId, IsActive)
OUTPUT INSERTED.UserId
VALUES (@Username, @PasswordEnc, @RoleId, @MemberId, @IsActive)";

            return await _db.QuerySingleAsync<long>(sql, user);
        }

        public async Task UpdateUserAsync(long userId, long roleId, bool isActive)
        {
            await EnsureOpenAsync();
            const string sql = "UPDATE dbo.AuthUsers SET RoleId=@roleId, IsActive=@isActive WHERE UserId=@userId";
            await _db.ExecuteAsync(sql, new { userId, roleId, isActive });
        }

        public async Task UpdateUserPasswordAsync(long userId, string passwordEnc)
        {
            await EnsureOpenAsync();
            const string sql = "UPDATE dbo.AuthUsers SET PasswordEnc=@passwordEnc WHERE UserId=@userId";
            await _db.ExecuteAsync(sql, new { userId, passwordEnc });
        }

        public async Task UpdateUserPasswordAsync(long userId, string passwordEnc, string? passwordViewEnc)
        {
            await EnsureOpenAsync();
            const string sql = "UPDATE dbo.AuthUsers SET PasswordEnc=@passwordEnc, PasswordViewEnc=@passwordViewEnc WHERE UserId=@userId";
            await _db.ExecuteAsync(sql, new { userId, passwordEnc, passwordViewEnc });
        }

        public async Task TouchLoginAsync(long userId)
        {
            await EnsureOpenAsync();
            await _db.ExecuteAsync("UPDATE dbo.AuthUsers SET LastLoginAt = GETUTCDATE(), LastSeenAt = GETUTCDATE() WHERE UserId=@userId", new { userId });
        }

        public async Task TouchSeenAsync(long userId)
        {
            await EnsureOpenAsync();
            await _db.ExecuteAsync("UPDATE dbo.AuthUsers SET LastSeenAt = GETUTCDATE() WHERE UserId=@userId", new { userId });
        }

        public async Task<AuthUser?> GetUserByMemberIdAsync(long memberId)
        {
            await EnsureOpenAsync();
            const string sql = @"
SELECT TOP 1 u.*, m.LastName AS MemberLastName, r.RoleId, r.Name, r.IsSystem, r.IsActive, r.CreatedAt
FROM dbo.AuthUsers u
LEFT JOIN dbo.Members m ON m.MemberId = u.MemberId
INNER JOIN dbo.AuthRoles r ON r.RoleId = u.RoleId
WHERE u.MemberId = @memberId";

            var rows = await _db.QueryAsync<AuthUser, AuthRole, AuthUser>(
                sql,
                (u, r) => { u.Role = r; return u; },
                new { memberId },
                splitOn: "RoleId");

            return rows.FirstOrDefault();
        }

        public async Task<bool> AnyUsersAsync()
        {
            await EnsureOpenAsync();
            return await _db.QuerySingleAsync<int>("SELECT COUNT(1) FROM dbo.AuthUsers") > 0;
        }
    }
}
