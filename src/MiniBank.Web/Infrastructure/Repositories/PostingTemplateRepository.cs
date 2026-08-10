using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class PostingTemplateRepository
    {
        private readonly IDbConnection _db;
        public PostingTemplateRepository(IDbConnection db) { _db = db; }

        public async Task<IEnumerable<PostingTemplate>> GetAllAsync(bool onlyActive = true, byte? category = null)
        {
            var where = new List<string>();
            if (onlyActive) where.Add("IsActive = 1");
            if (category.HasValue) where.Add("CategoryCode = @category");
            var whereSql = where.Count > 0 ? (" WHERE " + string.Join(" AND ", where)) : string.Empty;
            var sql = $"SELECT PostingTemplateId, TemplateName, IsActive, CategoryCode FROM dbo.PostingTemplates{whereSql} ORDER BY TemplateName";
            return await _db.QueryAsync<PostingTemplate>(sql, new { category });
        }

        public async Task<int> CreateAsync(string templateName)
        {
            const string sql = @"INSERT INTO dbo.PostingTemplates (TemplateName, IsActive) OUTPUT INSERTED.PostingTemplateId VALUES (@templateName, 1)";
            return await _db.ExecuteScalarAsync<int>(sql, new { templateName });
        }

        // Overload with CategoryCode
        public async Task<int> CreateAsync(string templateName, byte? categoryCode)
        {
            const string sql = @"INSERT INTO dbo.PostingTemplates (TemplateName, IsActive, CategoryCode) OUTPUT INSERTED.PostingTemplateId VALUES (@templateName, 1, @categoryCode)";
            return await _db.ExecuteScalarAsync<int>(sql, new { templateName, categoryCode });
        }

        public async Task<bool> UpdateTemplateAsync(int postingTemplateId, string templateName, byte? categoryCode)
        {
            const string sql = @"UPDATE dbo.PostingTemplates SET TemplateName = @templateName, CategoryCode = @categoryCode WHERE PostingTemplateId = @postingTemplateId";
            var rows = await _db.ExecuteAsync(sql, new { postingTemplateId, templateName, categoryCode });
            return rows > 0;
        }

        public async Task<IEnumerable<PostingTemplateLine>> GetLinesAsync(int postingTemplateId)
        {
            const string sql = @"
SELECT ptl.[PostingTemplateLineId], ptl.[PostingTemplateId], ptl.[LineNo], ptl.[AccountId], ptl.[OperationalAccountId], ptl.[Side], ptl.[AmountSource], ptl.[FixedAmount], ptl.[AllowMemberDimension],
       a.[Code] AS AccountCode, a.[Name] AS AccountName
FROM dbo.[PostingTemplateLines] ptl
JOIN dbo.[Accounts] a ON a.[AccountId] = ptl.[AccountId]
WHERE ptl.[PostingTemplateId] = @postingTemplateId
ORDER BY ptl.[LineNo]";
            return await _db.QueryAsync<PostingTemplateLine>(sql, new { postingTemplateId });
        }

        public async Task<int> AddLineAsync(PostingTemplateLine line)
        {
            const string sql = @"
INSERT INTO dbo.PostingTemplateLines ([PostingTemplateId], [LineNo], [AccountId], [OperationalAccountId], [Side], [AmountSource], [FixedAmount], [AllowMemberDimension])
OUTPUT INSERTED.PostingTemplateLineId
VALUES (@PostingTemplateId, @LineNo, @AccountId, @OperationalAccountId, @Side, @AmountSource, @FixedAmount, @AllowMemberDimension)";
            return await _db.ExecuteScalarAsync<int>(sql, line);
        }

        public async Task<bool> UpdateLineAsync(PostingTemplateLine line)
        {
            const string sql = @"
UPDATE dbo.PostingTemplateLines 
SET [LineNo] = @LineNo, 
    [AccountId] = @AccountId, 
    [OperationalAccountId] = @OperationalAccountId,
    [Side] = @Side, 
    [AmountSource] = @AmountSource, 
    [FixedAmount] = @FixedAmount,
    [AllowMemberDimension] = @AllowMemberDimension
WHERE [PostingTemplateLineId] = @PostingTemplateLineId";
            var rows = await _db.ExecuteAsync(sql, line);
            return rows > 0;
        }

        // Check if a posting template is referenced by any transactions or allocations
        public async Task<bool> IsTemplateUsedAsync(int postingTemplateId)
        {
            const string sql = @"
                SELECT CASE WHEN EXISTS(
                    SELECT 1 FROM dbo.Transactions WHERE PostingTemplateId = @postingTemplateId
                    UNION ALL
                    SELECT 1 FROM dbo.HouseholdReceiptAllocations WHERE PostingTemplateId = @postingTemplateId
                ) THEN 1 ELSE 0 END";
            var used = await _db.ExecuteScalarAsync<int>(sql, new { postingTemplateId });
            return used == 1;
        }

        public async Task<bool> DeleteLineAsync(int postingTemplateLineId)
        {
            const string sql = "DELETE FROM dbo.PostingTemplateLines WHERE PostingTemplateLineId = @postingTemplateLineId";
            var rows = await _db.ExecuteAsync(sql, new { postingTemplateLineId });
            return rows > 0;
        }

        public async Task<bool> DeleteTemplateAsync(int postingTemplateId)
        {
            // حذف تمام خطوط الگو ابتدا (به دلیل Foreign Key)
            const string deleteLinesSQL = "DELETE FROM dbo.PostingTemplateLines WHERE PostingTemplateId = @postingTemplateId";
            await _db.ExecuteAsync(deleteLinesSQL, new { postingTemplateId });

            // حذف خود الگو
            const string deleteTemplateSQL = "DELETE FROM dbo.PostingTemplates WHERE PostingTemplateId = @postingTemplateId";
            var rows = await _db.ExecuteAsync(deleteTemplateSQL, new { postingTemplateId });
            return rows > 0;
        }

        // Check if an account is referenced by any posting template line
        public async Task<PostingTemplate?> GetByIdAsync(int postingTemplateId)
        {
            const string sql = "SELECT PostingTemplateId, TemplateName, IsActive, CategoryCode FROM dbo.PostingTemplates WHERE PostingTemplateId = @postingTemplateId";
            return await _db.QueryFirstOrDefaultAsync<PostingTemplate>(sql, new { postingTemplateId });
        }

        public async Task<bool> IsAccountUsedInTemplatesAsync(long accountId)
        {
            const string sql = @"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.PostingTemplateLines WHERE AccountId = @accountId) THEN 1 ELSE 0 END";
            var used = await _db.ExecuteScalarAsync<int>(sql, new { accountId });
            return used == 1;
        }

        // Optional: return count of usages
        public async Task<int> CountAccountUsagesAsync(long accountId)
        {
            const string sql = @"SELECT COUNT(1) FROM dbo.PostingTemplateLines WHERE AccountId = @accountId";
            return await _db.ExecuteScalarAsync<int>(sql, new { accountId });
        }
    }
}
