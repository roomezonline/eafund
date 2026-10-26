using Dapper;
using System.Data;
using System.Data.Common;
using MiniBank.Web.Models;
using MiniBank.Web.Utilities;
using Microsoft.Data.SqlClient;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class TrialBalanceRepository
    {
        public class AccountInfo
        {
            public long AccountId { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public byte Level { get; set; }
            public long? ParentAccountId { get; set; }
            public long? MemberId { get; set; }
        }

        private readonly string _connectionString;
        
        public TrialBalanceRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string not found");
        }
        
        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        /// <summary>
        /// دریافت مسیر هرمی یک حساب
        /// </summary>
        public async Task<IEnumerable<long>> GetAccountHierarchyPathAsync(string accountCode)
        {
            using var connection = CreateConnection();
            
            var sql = @"
                WITH AccountHierarchy AS (
                    -- شروع از حساب مورد نظر
                    SELECT AccountId, ParentAccountId, Level
                    FROM dbo.Accounts 
                    WHERE Code = @accountCode AND IsActive = 1
                    
                    UNION ALL
                    
                    -- پیدا کردن والدین
                    SELECT a.AccountId, a.ParentAccountId, a.Level
                    FROM dbo.Accounts a
                    INNER JOIN AccountHierarchy ah ON a.AccountId = ah.ParentAccountId
                    WHERE a.IsActive = 1
                )
                SELECT AccountId FROM AccountHierarchy
                ORDER BY Level";
                
            var result = await connection.QueryAsync<long>(sql, new { accountCode });
            return result;
        }

        /// <summary>
        /// دریافت تراز بر اساس سطح
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> GetByLevelAsync(byte level, long? parentId = null, bool showOnlyWithBalance = true, DateTime? from = null, DateTime? to = null, IEnumerable<long>? hierarchyFilter = null)
        {
            using var connection = CreateConnection();
            
            var sql = @"
                SELECT 
                    a.AccountId,
                    a.Code,
                    CASE 
                        WHEN a.Level = 1 THEN COALESCE(at.Name, a.Name)
                        ELSE a.Name
                    END AS Name,
                    a.Level,
                    a.AccountType,
                    a.ParentAccountId,
                    
                    -- محاسبه بدهکار: مستقیم + از طریق ParentAccountId
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0) +
                    ISNULL(SUM(CASE WHEN j2.JournalId IS NOT NULL AND (@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl2.Debit ELSE 0 END), 0) AS DebitBalance,
                    
                    -- محاسبه بستانکار: مستقیم + از طریق ParentAccountId
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0) +
                    ISNULL(SUM(CASE WHEN j2.JournalId IS NOT NULL AND (@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl2.Credit ELSE 0 END), 0) AS CreditBalance,
                    
                    CASE WHEN EXISTS(
                        SELECT 1 FROM dbo.Accounts child 
                        WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                    ) THEN 1 ELSE 0 END AS HasChildren
                FROM dbo.Accounts a
                LEFT JOIN dbo.AccountTypes at ON (at.AccountTypeId = a.AccountType OR at.Code = CAST(a.AccountType AS NVARCHAR(50)))
                LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                LEFT JOIN dbo.JournalLines jl2 ON (
                    jl2.ParentAccountId = a.AccountId OR
                    jl2.ParentAccountId IN (SELECT AccountId FROM dbo.Accounts WHERE ParentAccountId = a.AccountId AND IsActive = 1) OR
                    jl2.ParentAccountId IN (SELECT child.AccountId FROM dbo.Accounts parent INNER JOIN dbo.Accounts child ON child.ParentAccountId = parent.AccountId WHERE parent.ParentAccountId = a.AccountId AND parent.IsActive = 1 AND child.IsActive = 1)
                )
                LEFT JOIN dbo.vw_ApprovedJournals j2 ON jl2.JournalId = j2.JournalId
                WHERE a.IsActive = 1 
                    AND a.Level = @level 
                    AND (@parentId IS NULL OR a.ParentAccountId = @parentId)
                    AND (@from IS NULL OR j.OccurredAt >= @from OR j.OccurredAt IS NULL)
                    AND (@from IS NULL OR j2.OccurredAt >= @from OR j2.OccurredAt IS NULL)
                    AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to) OR j.OccurredAt IS NULL)
                    AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to) OR j2.OccurredAt IS NULL)";

            // اضافه کردن فیلتر هرمی در صورت وجود
            if (hierarchyFilter != null && hierarchyFilter.Any())
            {
                sql += " AND a.AccountId IN @hierarchyFilter";
            }

            sql += @"
                GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId, at.Name";
            
            // اضافه کردن HAVING برای فیلتر حساب‌های بدون مانده (مانده صفر = بدهکار و بستانکار برابر)
            if (showOnlyWithBalance)
            {
                sql += @"
                HAVING (
                    ISNULL(SUM(CASE WHEN (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0) +
                    ISNULL(SUM(CASE WHEN (@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl2.Debit ELSE 0 END), 0)
                    <>
                    ISNULL(SUM(CASE WHEN (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0) +
                    ISNULL(SUM(CASE WHEN (@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl2.Credit ELSE 0 END), 0)
                )";
            }

            sql += " ORDER BY a.Code";
            
            if (hierarchyFilter != null && hierarchyFilter.Any())
            {
                var parametersWithFilter = new { level, parentId, showOnlyWithBalance, from, to, hierarchyFilter };
                return await connection.QueryAsync<TrialBalanceNode>(sql, parametersWithFilter);
            }
            else
            {
                var parameters = new { level, parentId, showOnlyWithBalance, from, to };
                return await connection.QueryAsync<TrialBalanceNode>(sql, parameters);
            }
        }

        /// <summary>
        /// دریافت تفصیلی‌های یک معین از طریق تراکنش‌های (JournalLines.ParentAccountId)
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> GetMoeenTafsiliListAsync(long moeenId, bool showOnlyWithBalance, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    a.AccountId,
                    a.Code,
                    a.Name,
                    a.Level,
                    a.AccountType,
                    a.ParentAccountId,
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0) AS DebitBalance,
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0) AS CreditBalance,
                    0 AS HasChildren
                FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                WHERE jl.ParentAccountId = @moeenId
                    AND a.Level = 4
                    AND a.IsActive = 1
                    AND (@from IS NULL OR j.OccurredAt >= @from)
                    AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))
                GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId";

            if (showOnlyWithBalance)
            {
                sql += @"
                HAVING (
                    ISNULL(SUM(CASE WHEN (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0)
                    <>
                    ISNULL(SUM(CASE WHEN (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0)
                )";
            }

            sql += " ORDER BY a.Code";

            return await connection.QueryAsync<TrialBalanceNode>(sql, new { moeenId, from, to });
        }

        /// <summary>
        /// دریافت فرزندان یک حساب
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> GetChildrenAsync(long parentId, DateTime? from = null, DateTime? to = null, IEnumerable<long>? hierarchyFilter = null)
        {
            using var connection = CreateConnection();
            
            // ابتدا سطح والد را پیدا کنیم
            var parentLevel = await connection.QueryFirstOrDefaultAsync<byte?>(
                "SELECT Level FROM dbo.Accounts WHERE AccountId = @parentId", new { parentId });
            
            if (!parentLevel.HasValue) return Enumerable.Empty<TrialBalanceNode>();
            
            // اگر سطح 4 باشد، فرزندان مستقیم را برگردان (نه از GetByLevelAsync)
            if (parentLevel.Value == 4)
            {
                var sql = @"
                SELECT 
                    a.AccountId,
                    a.Code,
                    a.Name,
                    a.Level,
                    a.AccountType,
                    a.ParentAccountId,
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0) +
                    ISNULL(SUM(CASE WHEN j2.JournalId IS NOT NULL AND (@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl2.Debit ELSE 0 END), 0) AS DebitBalance,
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0) +
                    ISNULL(SUM(CASE WHEN j2.JournalId IS NOT NULL AND (@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl2.Credit ELSE 0 END), 0) AS CreditBalance,
                    CASE WHEN EXISTS(
                        SELECT 1 FROM dbo.Accounts child 
                        WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                    ) THEN 1 
                    WHEN EXISTS(
                        SELECT 1 FROM dbo.JournalLines jl2 
                        WHERE jl2.AccountId = a.AccountId
                    ) THEN 1 
                    ELSE 0 END AS HasChildren
                FROM dbo.Accounts a
                LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                LEFT JOIN dbo.JournalLines jl2 ON jl2.ParentAccountId = a.AccountId
                LEFT JOIN dbo.vw_ApprovedJournals j2 ON jl2.JournalId = j2.JournalId
                WHERE a.IsActive = 1 AND a.ParentAccountId = @parentId";

                // اضافه کردن فیلتر هرمی در صورت وجود
                if (hierarchyFilter != null && hierarchyFilter.Any())
                {
                    sql += " AND a.AccountId IN @hierarchyFilter";
                }

                sql += @"
                GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId
                ORDER BY a.Code";
                
                if (hierarchyFilter != null && hierarchyFilter.Any())
                {
                    return await connection.QueryAsync<TrialBalanceNode>(sql, new { parentId, from, to, hierarchyFilter });
                }
                else
                {
                    return await connection.QueryAsync<TrialBalanceNode>(sql, new { parentId, from, to });
                }
            }
            
            var childLevel = (byte)(parentLevel.Value + 1);

            // برای سطوح 1 تا 3، از منطق استاندارد GetByLevelAsync استفاده کن تا هم AccountId و هم ParentAccountId پوشش داده شود
            if (hierarchyFilter != null && hierarchyFilter.Any())
                return await GetByLevelAsync(childLevel, parentId, true, from, to, hierarchyFilter);
            return await GetByLevelAsync(childLevel, parentId, true, from, to, null);
            
        }

        /// <summary>
        /// دریافت فرزندان یک حساب برای عضو خاص (در حالت جستجو)
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> GetChildrenByMemberAsync(long parentId, int memberId, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            
            // ابتدا سطح والد را پیدا کنیم
            var parentLevel = await connection.QueryFirstOrDefaultAsync<byte?>(
                "SELECT Level FROM dbo.Accounts WHERE AccountId = @parentId", new { parentId });
            
            if (!parentLevel.HasValue) return Enumerable.Empty<TrialBalanceNode>();
            
            // اگر سطح 4 باشد، فرزندان مستقیم را برگردان (فقط برای عضو مشخص)
            if (parentLevel.Value == 4)
            {
                var sql = @"
                SELECT 
                    a.AccountId,
                    a.Code,
                    a.Name,
                    a.Level,
                    a.AccountType,
                    a.ParentAccountId,
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND ((@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl.Debit ELSE 0 END), 0) AS DebitBalance,
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND ((@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl.Credit ELSE 0 END), 0) AS CreditBalance,
                    CASE WHEN EXISTS(
                        SELECT 1 FROM dbo.Accounts child 
                        WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                    ) THEN 1 
                    WHEN EXISTS(
                        SELECT 1 FROM dbo.JournalLines jl2 
                        WHERE jl2.AccountId = a.AccountId
                    ) THEN 1 
                    ELSE 0 END AS HasChildren
                FROM dbo.Accounts a
                LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                WHERE a.IsActive = 1 AND a.ParentAccountId = @parentId
                    AND a.MemberId = @memberId
                GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId
                ORDER BY a.Code";
                
                return await connection.QueryAsync<TrialBalanceNode>(sql, new { parentId, memberId, from, to });
            }
            
            // برای سطوح دیگر، فرزندانی که تراکنش برای این عضو دارند با مانده تجمیعی
            var childLevel = (byte)(parentLevel.Value + 1);
            
            var sql2 = @"
                WITH MemberChildren AS (
                    SELECT 
                        a.AccountId,
                        a.Code,
                        a.Name,
                        a.Level,
                        a.AccountType,
                        a.ParentAccountId,
                        -- مانده مستقیم + فرزندان + نوه‌ها برای عضو مشخص (بر اساس حساب تفضیلی لینک شده به عضو)
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND ((@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl.Debit ELSE 0 END), 0) +
                        ISNULL((
                            SELECT SUM(CASE WHEN j2.JournalId IS NOT NULL AND ((@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl2.Debit ELSE 0 END)
                            FROM dbo.Accounts c
                            LEFT JOIN dbo.JournalLines jl2 ON c.AccountId = jl2.AccountId
                            LEFT JOIN dbo.vw_ApprovedJournals j2 ON jl2.JournalId = j2.JournalId
                            WHERE c.ParentAccountId = a.AccountId AND c.IsActive = 1
                        ), 0) +
                        ISNULL((
                            SELECT SUM(CASE WHEN j3.JournalId IS NOT NULL AND ((@from IS NULL OR j3.OccurredAt >= @from) AND (@to IS NULL OR j3.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl3.Debit ELSE 0 END)
                            FROM dbo.Accounts gc
                            LEFT JOIN dbo.JournalLines jl3 ON gc.AccountId = jl3.AccountId
                            LEFT JOIN dbo.vw_ApprovedJournals j3 ON jl3.JournalId = j3.JournalId
                            INNER JOIN dbo.Accounts c ON gc.ParentAccountId = c.AccountId
                            WHERE c.ParentAccountId = a.AccountId AND gc.IsActive = 1 AND c.IsActive = 1
                        ), 0) AS DebitBalance,
                        
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND ((@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl.Credit ELSE 0 END), 0) +
                        ISNULL((
                            SELECT SUM(CASE WHEN j2.JournalId IS NOT NULL AND ((@from IS NULL OR j2.OccurredAt >= @from) AND (@to IS NULL OR j2.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl2.Credit ELSE 0 END)
                            FROM dbo.Accounts c
                            LEFT JOIN dbo.JournalLines jl2 ON c.AccountId = jl2.AccountId
                            LEFT JOIN dbo.vw_ApprovedJournals j2 ON jl2.JournalId = j2.JournalId
                            WHERE c.ParentAccountId = a.AccountId AND c.IsActive = 1
                        ), 0) +
                        ISNULL((
                            SELECT SUM(CASE WHEN j3.JournalId IS NOT NULL AND ((@from IS NULL OR j3.OccurredAt >= @from) AND (@to IS NULL OR j3.OccurredAt < DATEADD(DAY, 1, @to))) THEN jl3.Credit ELSE 0 END)
                            FROM dbo.Accounts gc
                            LEFT JOIN dbo.JournalLines jl3 ON gc.AccountId = jl3.AccountId
                            LEFT JOIN dbo.vw_ApprovedJournals j3 ON jl3.JournalId = j3.JournalId
                            INNER JOIN dbo.Accounts c ON gc.ParentAccountId = c.AccountId
                            WHERE c.ParentAccountId = a.AccountId AND gc.IsActive = 1 AND c.IsActive = 1
                        ), 0) AS CreditBalance,
                        CASE WHEN EXISTS(
                            SELECT 1 FROM dbo.Accounts child 
                            WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                        ) THEN 1 
                        WHEN EXISTS(
                            SELECT 1 FROM dbo.JournalLines jl2 
                            WHERE jl2.AccountId = a.AccountId
                        ) THEN 1 
                        ELSE 0 END AS HasChildren
                    FROM dbo.Accounts a
                    LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                    LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                    WHERE a.IsActive = 1 
                        AND a.Level = @childLevel
                        AND a.ParentAccountId = @parentId
                        AND a.MemberId = @memberId
                    GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId
                )
                SELECT * FROM MemberChildren
                WHERE DebitBalance != 0 OR CreditBalance != 0
                ORDER BY Code";
            
            return await connection.QueryAsync<TrialBalanceNode>(sql2, new { parentId, memberId, childLevel, from, to });
        }

        /// <summary>
        /// دریافت تراز کامل (همه سطوح)
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> GetFullTrialBalanceAsync()
        {
            using var connection = CreateConnection();
            
            var sql = @"
                WITH AccountBalances AS (
                    SELECT 
                        a.AccountId,
                        a.Code,
                        a.Name,
                        a.Level,
                        a.AccountType,
                        a.ParentAccountId,
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL THEN jl.Debit ELSE 0 END), 0) AS DebitBalance,
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL THEN jl.Credit ELSE 0 END), 0) AS CreditBalance,
                        CASE WHEN EXISTS(
                            SELECT 1 FROM dbo.Accounts child 
                            WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                        ) THEN 1 
                        WHEN EXISTS(
                            SELECT 1 FROM dbo.JournalLines jl2 
                            WHERE jl2.AccountId = a.AccountId
                        ) AND NOT EXISTS(
                            SELECT 1 FROM dbo.Accounts child 
                            WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                        ) THEN 1 
                        ELSE 0 END AS HasChildren
                    FROM dbo.Accounts a
                    LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                    LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                    WHERE a.IsActive = 1
                    GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId
                )
                SELECT * FROM AccountBalances
                ORDER BY Level, Code";
            
            return await connection.QueryAsync<TrialBalanceNode>(sql);
        }

        /// <summary>
        /// دریافت خلاصه تراز (فقط مجاميع)
        /// </summary>
        public async Task<TrialBalanceSummary> GetSummaryAsync()
        {
            using var connection = CreateConnection();
            
            var sql = @"
                SELECT 
                    SUM(CASE WHEN j.JournalId IS NOT NULL AND jl.Debit > 0 THEN jl.Debit ELSE 0 END) AS TotalDebits,
                    SUM(CASE WHEN j.JournalId IS NOT NULL AND jl.Credit > 0 THEN jl.Credit ELSE 0 END) AS TotalCredits,
                    COUNT(DISTINCT a.AccountId) AS TotalAccounts,
                    COUNT(DISTINCT j.JournalId) AS TotalJournals
                FROM dbo.Accounts a
                LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                WHERE a.IsActive = 1";
            
            return await connection.QuerySingleAsync<TrialBalanceSummary>(sql);
        }

        /// <summary>
        /// جستجو در تراز
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> SearchAsync(string searchTerm, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            
            var sql = @"
                WITH AccountBalances AS (
                    SELECT 
                        a.AccountId,
                        a.Code,
                        a.Name,
                        a.Level,
                        a.AccountType,
                        a.ParentAccountId,
                        ISNULL(SUM(jl.Debit), 0) AS DebitBalance,
                        ISNULL(SUM(jl.Credit), 0) AS CreditBalance,
                        CASE WHEN EXISTS(
                            SELECT 1 FROM dbo.Accounts child 
                            WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                        ) THEN 1 
                        WHEN EXISTS(
                            SELECT 1 FROM dbo.JournalLines jl2 
                            WHERE jl2.AccountId = a.AccountId
                        ) AND NOT EXISTS(
                            SELECT 1 FROM dbo.Accounts child 
                            WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                        ) THEN 1 
                        ELSE 0 END AS HasChildren
                    FROM dbo.Accounts a
                    LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                    LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                    WHERE a.IsActive = 1 
                        AND (a.Code LIKE @searchPattern OR a.Name LIKE @searchPattern)
                    GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId
                )
                SELECT * FROM AccountBalances
                ORDER BY Level, Code";
            
            return await connection.QueryAsync<TrialBalanceNode>(sql, new { searchPattern = $"%{searchTerm}%", from, to });
        }

        /// <summary>
        /// دریافت تراکنش‌های یک حساب
        /// </summary>
        public async Task<IEnumerable<JournalLineDetail>> GetAccountTransactionsAsync(long accountId, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            
            var sql = @"
                SELECT 
                    jl.JournalLineId,
                    jl.JournalId,
                    j.DocumentNumber,
                    jl.AccountId,
                    jl.MemberId,
                    jl.Debit,
                    jl.Credit,
                    jl.[LineNo],
                    ISNULL(jl.Description, t.Notes) AS Description,
                    j.OccurredAt,
                    j.CreatedBy,
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')) AS Notes,
                    ISNULL(tm.FirstName, N'') + N' ' + ISNULL(tm.LastName, N'') AS MemberName,
                    a.Code AS AccountCode,
                    a.Name AS AccountName,
                    dt.Title AS DepositTypeName,
                    t.AmountTotal AS TransactionAmount,
                    pt.TemplateName AS PostingTemplateName,
                    cp.CounterpartCode,
                    cp.CounterpartName,

                    COALESCE(hr1.DocumentType, hr2.DocumentType) as DocumentType,
                    COALESCE(hr1.TransactionType, hr2.TransactionType) as TransactionTypeCode,
                    COALESCE(hr1.PayChannel, hr2.PayChannel) as PayChannel,
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName) as CounterpartyName,
                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END as DisplayName,

                    CASE 
                        WHEN jl.Debit > 0 THEN N'بدهکار'
                        WHEN jl.Credit > 0 THEN N'بستانکار'
                        ELSE N'متعادل'
                    END AS TransactionType
                FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                LEFT JOIN dbo.Transactions t ON j.TransactionId = t.TransactionId
                LEFT JOIN dbo.HouseholdReceipts hr1 ON hr1.HouseholdReceiptId = t.ParentReceiptId
                LEFT JOIN dbo.HouseholdReceipts hr2 ON hr2.ExternalRef = CONCAT('J:', j.JournalId)
                LEFT JOIN dbo.Households h ON (h.HouseholdId = hr1.TargetHouseholdId OR h.HouseholdId = hr2.TargetHouseholdId)
                LEFT JOIN dbo.Members tm ON t.MemberId = tm.MemberId
                LEFT JOIN dbo.DepositTypes dt ON t.DepositTypeId = dt.DepositTypeId
                LEFT JOIN dbo.PostingTemplates pt ON t.PostingTemplateId = pt.PostingTemplateId
                OUTER APPLY (
                    SELECT TOP 1
                        ca.Code AS CounterpartCode,
                        ca.Name AS CounterpartName
                    FROM dbo.JournalLines jl2
                    INNER JOIN dbo.Accounts ca ON jl2.AccountId = ca.AccountId
                    WHERE jl2.JournalId = jl.JournalId
                        AND jl2.[LineNo] != jl.[LineNo]
                    ORDER BY jl2.[LineNo]
                ) cp
                WHERE jl.AccountId = @accountId
                    AND (@from IS NULL OR j.OccurredAt >= @from)
                    AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))
                ORDER BY j.OccurredAt DESC, jl.[LineNo]";
            
            return await connection.QueryAsync<JournalLineDetail>(sql, new { accountId, from, to });
        }

        /// <summary>
        /// دریافت تراکنش‌های یک حساب برای عضو خاص
        /// </summary>
        public async Task<IEnumerable<JournalLineDetail>> GetAccountTransactionsByMemberAsync(long accountId, int memberId, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            
            var sql = @"
                SELECT 
                    jl.JournalLineId,
                    jl.JournalId,
                    jl.AccountId,
                    jl.MemberId,
                    jl.Debit,
                    jl.Credit,
                    jl.[LineNo],
                    ISNULL(jl.Description, t.Notes) AS Description,
                    j.OccurredAt,
                    j.CreatedBy,
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')) AS Notes,
                    ISNULL(tm.FirstName, N'') + N' ' + ISNULL(tm.LastName, N'') AS MemberName,
                    a.Code AS AccountCode,
                    a.Name AS AccountName,
                    dt.Title AS DepositTypeName,
                    t.AmountTotal AS TransactionAmount,
                    pt.TemplateName AS PostingTemplateName,
                    cp.CounterpartCode,
                    cp.CounterpartName,

                    COALESCE(hr1.DocumentType, hr2.DocumentType) as DocumentType,
                    COALESCE(hr1.TransactionType, hr2.TransactionType) as TransactionTypeCode,
                    COALESCE(hr1.PayChannel, hr2.PayChannel) as PayChannel,
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName) as CounterpartyName,
                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END as DisplayName,

                    CASE 
                        WHEN jl.Debit > 0 THEN N'بدهکار'
                        WHEN jl.Credit > 0 THEN N'بستانکار'
                        ELSE N'متعادل'
                    END AS TransactionType
                FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                LEFT JOIN dbo.Transactions t ON j.TransactionId = t.TransactionId
                LEFT JOIN dbo.HouseholdReceipts hr1 ON hr1.HouseholdReceiptId = t.ParentReceiptId
                LEFT JOIN dbo.HouseholdReceipts hr2 ON hr2.ExternalRef = CONCAT('J:', j.JournalId)
                LEFT JOIN dbo.Households h ON (h.HouseholdId = hr1.TargetHouseholdId OR h.HouseholdId = hr2.TargetHouseholdId)
                LEFT JOIN dbo.Members tm ON t.MemberId = tm.MemberId
                LEFT JOIN dbo.DepositTypes dt ON t.DepositTypeId = dt.DepositTypeId
                LEFT JOIN dbo.PostingTemplates pt ON t.PostingTemplateId = pt.PostingTemplateId
                OUTER APPLY (
                    SELECT TOP 1
                        ca.Code AS CounterpartCode,
                        ca.Name AS CounterpartName
                    FROM dbo.JournalLines jl2
                    INNER JOIN dbo.Accounts ca ON jl2.AccountId = ca.AccountId
                    WHERE jl2.JournalId = jl.JournalId
                        AND jl2.[LineNo] != jl.[LineNo]
                    ORDER BY jl2.[LineNo]
                ) cp
                WHERE jl.AccountId = @accountId
                    AND (jl.MemberId = @memberId OR t.MemberId = @memberId)
                    AND (@from IS NULL OR j.OccurredAt >= @from)
                    AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))
                ORDER BY j.OccurredAt DESC, jl.[LineNo]";
            
            return await connection.QueryAsync<JournalLineDetail>(sql, new { accountId, memberId, from, to });
        }

        public class JournalHeaderItem
        {
            public long JournalId { get; set; }
            public int? DocumentNumber { get; set; }
            public DateTime OccurredAt { get; set; }
            public string Notes { get; set; } = string.Empty;
            public string CreatedBy { get; set; } = string.Empty;
            public byte? DocumentType { get; set; }
            public string? TransactionType { get; set; }
            public byte? PayChannel { get; set; }
            public string? CounterpartyName { get; set; }
            public string DisplayName { get; set; } = string.Empty;
            public decimal TransactionAmount { get; set; }
        }

        public class JournalLineHistoryItem
        {
            public long JournalLineId { get; set; }
            public long LineNumber { get; set; }
            public string MoeenCode { get; set; } = string.Empty;
            public string MoeenName { get; set; } = string.Empty;
            public string SubsidiaryCode { get; set; } = string.Empty;
            public string SubsidiaryName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
            public long JournalId { get; set; }
        }

        public async Task<List<JournalHeaderItem>> GetJournalHeadersByAccountAsync(long accountId, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    j.JournalId,
                    j.DocumentNumber,
                    CAST(j.OccurredAt AS DATE) as OccurredAt,
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')) AS Notes,
                    j.CreatedBy,

                    COALESCE(hr1.DocumentType, hr2.DocumentType) as DocumentType,
                    COALESCE(hr1.TransactionType, hr2.TransactionType) as TransactionType,
                    COALESCE(hr1.PayChannel, hr2.PayChannel) as PayChannel,
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName) as CounterpartyName,

                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END as DisplayName,

                    ISNULL(COALESCE(t.AmountTotal, hr1.AmountTotal, hr2.AmountTotal), 0) as TransactionAmount

                FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
                LEFT JOIN dbo.HouseholdReceipts hr1 ON hr1.HouseholdReceiptId = t.ParentReceiptId
                LEFT JOIN dbo.HouseholdReceipts hr2 ON hr2.ExternalRef = CONCAT('J:', j.JournalId)
                LEFT JOIN dbo.Households h ON (h.HouseholdId = hr1.TargetHouseholdId OR h.HouseholdId = hr2.TargetHouseholdId)
                WHERE jl.AccountId = @accountId
                    AND (@from IS NULL OR j.OccurredAt >= @from)
                    AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))
                GROUP BY 
                    j.JournalId,
                    j.DocumentNumber,
                    CAST(j.OccurredAt AS DATE),
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')),
                    j.CreatedBy,
                    COALESCE(hr1.DocumentType, hr2.DocumentType),
                    COALESCE(hr1.TransactionType, hr2.TransactionType),
                    COALESCE(hr1.PayChannel, hr2.PayChannel),
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName),
                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END,
                    ISNULL(COALESCE(t.AmountTotal, hr1.AmountTotal, hr2.AmountTotal), 0)
                ORDER BY OccurredAt DESC, j.JournalId DESC";

            var result = await connection.QueryAsync<JournalHeaderItem>(sql, new { accountId, from, to });
            return result.ToList();
        }

        public async Task<List<JournalHeaderItem>> GetJournalHeadersByAccountIncludingChildrenAsync(long rootAccountId, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                ;WITH acc AS (
                    SELECT a.AccountId
                    FROM dbo.Accounts a
                    WHERE a.AccountId = @rootAccountId AND a.IsActive = 1

                    UNION ALL

                    SELECT c.AccountId
                    FROM dbo.Accounts c
                    INNER JOIN acc p ON c.ParentAccountId = p.AccountId
                    WHERE c.IsActive = 1
                )
                SELECT
                    j.JournalId,
                    j.DocumentNumber,
                    CAST(j.OccurredAt AS DATE) as OccurredAt,
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')) AS Notes,
                    j.CreatedBy,

                    COALESCE(hr1.DocumentType, hr2.DocumentType) as DocumentType,
                    COALESCE(hr1.TransactionType, hr2.TransactionType) as TransactionType,
                    COALESCE(hr1.PayChannel, hr2.PayChannel) as PayChannel,
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName) as CounterpartyName,

                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END as DisplayName,

                    ISNULL(COALESCE(t.AmountTotal, hr1.AmountTotal, hr2.AmountTotal), 0) as TransactionAmount

                FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
                LEFT JOIN dbo.HouseholdReceipts hr1 ON hr1.HouseholdReceiptId = t.ParentReceiptId
                LEFT JOIN dbo.HouseholdReceipts hr2 ON hr2.ExternalRef = CONCAT('J:', j.JournalId)
                LEFT JOIN dbo.Households h ON (h.HouseholdId = hr1.TargetHouseholdId OR h.HouseholdId = hr2.TargetHouseholdId)
                WHERE EXISTS (
                        SELECT 1
                        FROM acc a
                        WHERE a.AccountId = jl.AccountId
                           OR a.AccountId = jl.ParentAccountId
                    )
                    AND (@from IS NULL OR j.OccurredAt >= @from)
                    AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))
                GROUP BY 
                    j.JournalId,
                    j.DocumentNumber,
                    CAST(j.OccurredAt AS DATE),
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')),
                    j.CreatedBy,
                    COALESCE(hr1.DocumentType, hr2.DocumentType),
                    COALESCE(hr1.TransactionType, hr2.TransactionType),
                    COALESCE(hr1.PayChannel, hr2.PayChannel),
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName),
                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END,
                    ISNULL(COALESCE(t.AmountTotal, hr1.AmountTotal, hr2.AmountTotal), 0)
                ORDER BY OccurredAt DESC, j.JournalId DESC
                OPTION (MAXRECURSION 200)";

            var result = await connection.QueryAsync<JournalHeaderItem>(sql, new { rootAccountId, from, to });
            return result.ToList();
        }

        public async Task<List<JournalHeaderItem>> GetJournalHeadersByMoeenAndTafsiliAsync(long moeenAccountId, long tafsiliAccountId, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    j.JournalId,
                    j.DocumentNumber,
                    CAST(j.OccurredAt AS DATE) as OccurredAt,
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')) AS Notes,
                    j.CreatedBy,

                    COALESCE(hr1.DocumentType, hr2.DocumentType) as DocumentType,
                    COALESCE(hr1.TransactionType, hr2.TransactionType) as TransactionType,
                    COALESCE(hr1.PayChannel, hr2.PayChannel) as PayChannel,
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName) as CounterpartyName,

                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END as DisplayName,

                    ISNULL(COALESCE(t.AmountTotal, hr1.AmountTotal, hr2.AmountTotal), 0) as TransactionAmount

                FROM dbo.JournalLines jl
                INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                LEFT JOIN dbo.Transactions t ON t.TransactionId = j.TransactionId
                LEFT JOIN dbo.HouseholdReceipts hr1 ON hr1.HouseholdReceiptId = t.ParentReceiptId
                LEFT JOIN dbo.HouseholdReceipts hr2 ON hr2.ExternalRef = CONCAT('J:', j.JournalId)
                LEFT JOIN dbo.Households h ON (h.HouseholdId = hr1.TargetHouseholdId OR h.HouseholdId = hr2.TargetHouseholdId)
                WHERE jl.ParentAccountId = @moeenAccountId
                    AND jl.AccountId = @tafsiliAccountId
                    AND (@from IS NULL OR j.OccurredAt >= @from)
                    AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))
                GROUP BY 
                    j.JournalId,
                    j.DocumentNumber,
                    CAST(j.OccurredAt AS DATE),
                    ISNULL(j.Notes, ISNULL(t.Notes, N'')),
                    j.CreatedBy,
                    COALESCE(hr1.DocumentType, hr2.DocumentType),
                    COALESCE(hr1.TransactionType, hr2.TransactionType),
                    COALESCE(hr1.PayChannel, hr2.PayChannel),
                    COALESCE(hr1.CounterpartyName, hr2.CounterpartyName),
                    CASE 
                        WHEN COALESCE(hr1.DocumentType, hr2.DocumentType) = 1 THEN 
                            COALESCE(h.Name, hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                        ELSE 
                            COALESCE(hr1.CounterpartyName, hr2.CounterpartyName, N'-')
                    END,
                    ISNULL(COALESCE(t.AmountTotal, hr1.AmountTotal, hr2.AmountTotal), 0)
                ORDER BY OccurredAt DESC, j.JournalId DESC";

            var result = await connection.QueryAsync<JournalHeaderItem>(sql, new { moeenAccountId, tafsiliAccountId, from, to });
            return result.ToList();
        }

        public class AccountBalanceItem
        {
            public long AccountId { get; set; }
            public byte AccountType { get; set; }
            public decimal DebitBalance { get; set; }
            public decimal CreditBalance { get; set; }
        }

        public async Task<AccountBalanceItem?> GetAccountBalanceByCodeAsync(string accountCode, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    a.AccountId,
                    a.AccountType,
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0) AS DebitBalance,
                    ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0) AS CreditBalance
                FROM dbo.Accounts a
                LEFT JOIN dbo.JournalLines jl ON (jl.AccountId = a.AccountId OR jl.ParentAccountId = a.AccountId)
                LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                WHERE a.Code = @accountCode AND a.IsActive = 1
                AND (j.JournalId IS NULL OR " + OpeningJournal.ExclusionSql() + @")
                GROUP BY a.AccountId, a.AccountType";

            return await connection.QuerySingleOrDefaultAsync<AccountBalanceItem?>(sql, new { accountCode, from, to });
        }

        public async Task<(long AccountId, string Code)?> GetParentInfoByAccountCodeAsync(string childCode)
        {
            using var connection = CreateConnection();

            var sql = @"SELECT p.AccountId, p.Code FROM dbo.Accounts c
                        INNER JOIN dbo.Accounts p ON c.ParentAccountId = p.AccountId
                        WHERE c.Code = @childCode AND c.IsActive = 1";
            var row = await connection.QuerySingleOrDefaultAsync<(long AccountId, string Code)?>(sql, new { childCode });
            return row;
        }

        public async Task<string?> GetParentCodeByAccountCodeAsync(string childCode)
        {
            using var connection = CreateConnection();

            var sql = @"SELECT p.Code FROM dbo.Accounts c
                        INNER JOIN dbo.Accounts p ON c.ParentAccountId = p.AccountId
                        WHERE c.Code = @childCode AND c.IsActive = 1";
            return await connection.QuerySingleOrDefaultAsync<string?>(sql, new { childCode });
        }

        public async Task<string?> GetAccountCodeByIdAsync(long accountId)
        {
            using var connection = CreateConnection();

            var sql = @"SELECT a.Code FROM dbo.Accounts a WHERE a.AccountId = @accountId";
            return await connection.QuerySingleOrDefaultAsync<string?>(sql, new { accountId });
        }

        public async Task<(string Code, string Name)?> GetAccountDisplayByIdAsync(long accountId)
        {
            using var connection = CreateConnection();

            var sql = @"SELECT a.Code, a.Name FROM dbo.Accounts a WHERE a.AccountId = @accountId";
            var result = await connection.QuerySingleOrDefaultAsync<(string Code, string Name)?>(sql, new { accountId });
            return result;
        }

        public async Task<List<JournalLineHistoryItem>> GetJournalLinesByJournalIdAsync(long journalId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    jl.JournalLineId,
                    jl.JournalLineId AS LineNumber,
                    CASE 
                        WHEN ma.AccountId IS NOT NULL THEN COALESCE(ma.Code, '-')
                        WHEN a.Level = 3 THEN COALESCE(a.Code, '-')
                        ELSE '-'
                    END AS MoeenCode,
                    CASE 
                        WHEN ma.AccountId IS NOT NULL THEN COALESCE(ma.Name, 'Unknown')
                        WHEN a.Level = 3 THEN COALESCE(a.Name, 'Unknown')
                        ELSE N'تفصیلی مستقل'
                    END AS MoeenName,
                    CASE 
                        WHEN a.Level = 4 THEN COALESCE(a.Code, '-')
                        ELSE '-'
                    END AS SubsidiaryCode,
                    CASE 
                        WHEN a.Level = 4 THEN COALESCE(a.Name, '-')
                        ELSE '-'
                    END AS SubsidiaryName,
                    ISNULL(jl.Description, N'') AS Description,
                    jl.Debit,
                    jl.Credit,
                    jl.JournalId
                FROM dbo.JournalLines jl
                LEFT JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                LEFT JOIN dbo.Accounts ma ON ma.AccountId = jl.ParentAccountId
                WHERE jl.JournalId = @journalId
                ORDER BY jl.[LineNo]";

            var result = await connection.QueryAsync<JournalLineHistoryItem>(sql, new { journalId });
            return result.ToList();
        }

        public async Task<Dictionary<long, List<JournalLineHistoryItem>>> GetJournalLinesByJournalIdsAsync(IEnumerable<long> journalIds)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    jl.JournalLineId,
                    jl.JournalLineId AS LineNumber,
                    CASE 
                        WHEN ma.AccountId IS NOT NULL THEN COALESCE(ma.Code, '-')
                        WHEN a.Level = 3 THEN COALESCE(a.Code, '-')
                        ELSE '-'
                    END AS MoeenCode,
                    CASE 
                        WHEN ma.AccountId IS NOT NULL THEN COALESCE(ma.Name, 'Unknown')
                        WHEN a.Level = 3 THEN COALESCE(a.Name, 'Unknown')
                        ELSE N'تفصیلی مستقل'
                    END AS MoeenName,
                    CASE 
                        WHEN a.Level = 4 THEN COALESCE(a.Code, '-')
                        ELSE '-'
                    END AS SubsidiaryCode,
                    CASE 
                        WHEN a.Level = 4 THEN COALESCE(a.Name, '-')
                        ELSE '-'
                    END AS SubsidiaryName,
                    ISNULL(jl.Description, N'') AS Description,
                    jl.Debit,
                    jl.Credit,
                    jl.JournalId
                FROM dbo.JournalLines jl
                LEFT JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                LEFT JOIN dbo.Accounts ma ON ma.AccountId = jl.ParentAccountId
                WHERE jl.JournalId IN @journalIds
                ORDER BY jl.JournalId, jl.[LineNo]";

            var lines = await connection.QueryAsync<JournalLineHistoryItem>(sql, new { journalIds });
            return lines.GroupBy(l => l.JournalId).ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// تشخیص تراکنش‌های مربوط به هر حساب تفصیلی
        /// </summary>
        public async Task<Dictionary<long, int>> GetTransactionCountByAccountAsync()
        {
            using var connection = CreateConnection();
            
            var sql = @"
                SELECT 
                    jl.AccountId,
                    COUNT(*) AS TransactionCount
                FROM dbo.JournalLines jl
                INNER JOIN dbo.Accounts a ON jl.AccountId = a.AccountId
                WHERE a.IsActive = 1
                GROUP BY jl.AccountId
                HAVING COUNT(*) > 0
                ORDER BY jl.AccountId";
            
            var results = await connection.QueryAsync<(long AccountId, int TransactionCount)>(sql);
            return results.ToDictionary(x => x.AccountId, x => x.TransactionCount);
        }

        /// <summary>
        /// بررسی جزئیات تراکنش‌ها برای تشخیص مشکل
        /// </summary>
        public async Task<IEnumerable<dynamic>> GetTransactionAnalysisAsync()
        {
            using var connection = CreateConnection();
            
            var sql = @"
                SELECT 
                    a.AccountId,
                    a.Code,
                    a.Name,
                    a.Level,
                    a.ParentAccountId,
                    COUNT(jl.JournalLineId) AS TransactionCount,
                    SUM(ISNULL(jl.Debit, 0)) AS TotalDebit,
                    SUM(ISNULL(jl.Credit, 0)) AS TotalCredit,
                    MIN(j.OccurredAt) AS FirstTransaction,
                    MAX(j.OccurredAt) AS LastTransaction
                FROM dbo.Accounts a
                LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                WHERE a.Level = 4
                GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.ParentAccountId
                ORDER BY TransactionCount DESC";
            
            return await connection.QueryAsync(sql);
        }

        /// <summary>
        /// جستجوی حساب‌های مربوط به کد عضو
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> SearchByMemberCodeAsync(string memberCode, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            
            var sql = @"
                WITH MemberAccounts AS (
                    SELECT 
                        a.AccountId,
                        a.Code,
                        a.Name,
                        a.Level,
                        a.AccountType,
                        a.ParentAccountId,
                        ISNULL(SUM(CASE WHEN (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0) AS DebitBalance,
                        ISNULL(SUM(CASE WHEN (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0) AS CreditBalance,
                        CASE WHEN EXISTS(
                            SELECT 1 FROM dbo.Accounts child 
                            WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                        ) THEN 1 
                        WHEN EXISTS(
                            SELECT 1 FROM dbo.JournalLines jl2 
                            WHERE jl2.AccountId = a.AccountId
                        ) THEN 1 
                        ELSE 0 END AS HasChildren
                    FROM dbo.Accounts a
                    LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                    LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                    WHERE a.IsActive = 1 
                        AND a.MemberId = @memberId
                        AND a.Level >= 3  -- فقط حساب‌های معین و تفضیلی
                    GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId
                )
                SELECT * FROM MemberAccounts
                WHERE DebitBalance != 0 OR CreditBalance != 0  -- فقط حساب‌های با مانده
                ORDER BY Code";
            
            return await connection.QueryAsync<TrialBalanceNode>(sql, new { memberId = int.Parse(memberCode), from, to });
        }

        /// <summary>
        /// جستجوی حساب‌های مربوط به کد تفضیلی
        /// </summary>
        public async Task<IEnumerable<TrialBalanceNode>> SearchBySubsidiaryCodeAsync(string subsidiaryCode, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            
            var sql = @"
                WITH SubsidiaryAccounts AS (
                    SELECT 
                        a.AccountId,
                        a.Code,
                        a.Name,
                        a.Level,
                        a.AccountType,
                        a.ParentAccountId,
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Debit ELSE 0 END), 0) AS DebitBalance,
                        ISNULL(SUM(CASE WHEN j.JournalId IS NOT NULL AND (@from IS NULL OR j.OccurredAt >= @from) AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to)) THEN jl.Credit ELSE 0 END), 0) AS CreditBalance,
                        CASE WHEN EXISTS(
                            SELECT 1 FROM dbo.Accounts child 
                            WHERE child.ParentAccountId = a.AccountId AND child.IsActive = 1
                        ) THEN 1 
                        WHEN EXISTS(
                            SELECT 1 FROM dbo.JournalLines jl2 
                            WHERE jl2.AccountId = a.AccountId
                        ) THEN 1 
                        ELSE 0 END AS HasChildren
                    FROM dbo.Accounts a
                    LEFT JOIN dbo.JournalLines jl ON a.AccountId = jl.AccountId
                    LEFT JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                    WHERE a.IsActive = 1 
                        AND (a.Code = @subsidiaryCode OR a.Code LIKE @subsidiaryCodePattern)
                    GROUP BY a.AccountId, a.Code, a.Name, a.Level, a.AccountType, a.ParentAccountId
                )
                SELECT * FROM SubsidiaryAccounts
                WHERE DebitBalance != 0 OR CreditBalance != 0  -- فقط حساب‌های با مانده
                ORDER BY Code";
            
            return await connection.QueryAsync<TrialBalanceNode>(sql, new { 
                subsidiaryCode, 
                subsidiaryCodePattern = subsidiaryCode + "%",
                from,
                to
            });
        }

        public async Task<AccountInfo?> GetAccountInfoAsync(long accountId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT TOP 1
                    a.AccountId,
                    a.Code,
                    a.Name,
                    a.Level,
                    a.ParentAccountId,
                    a.MemberId
                FROM dbo.Accounts a
                WHERE a.AccountId = @accountId AND a.IsActive = 1";

            return await connection.QueryFirstOrDefaultAsync<AccountInfo>(sql, new { accountId });
        }

        public async Task<IEnumerable<TrialBalanceNode>> GetMoeenByTafsiliAsync(long tafsiliAccountId, DateTime? from = null, DateTime? to = null, bool showOnlyWithBalance = true)
        {
            using var connection = CreateConnection();

            var tafsili = await GetAccountInfoAsync(tafsiliAccountId);
            if (tafsili == null) return Enumerable.Empty<TrialBalanceNode>();

            var sql = @"
                WITH IssuedTafsiliLines AS (
                    -- معیار: خود خط‌هایی که تفصیلی در آنها ثبت شده (AccountId=تفصیلی)
                    -- معین مرتبط طبق ساختار ثبت سند در سیستم: JournalLines.ParentAccountId
                    SELECT
                        jl.JournalId,
                        jl.ParentAccountId AS MoeenAccountId,
                        jl.Debit,
                        jl.Credit
                    FROM dbo.JournalLines jl
                    INNER JOIN dbo.vw_ApprovedJournals j ON j.JournalId = jl.JournalId
                    WHERE jl.AccountId = @tafsiliAccountId
                        AND (@from IS NULL OR j.OccurredAt >= @from)
                        AND (@to IS NULL OR j.OccurredAt < DATEADD(DAY, 1, @to))
                ), MoeenFromParentAgg AS (
                    SELECT
                        MoeenAccountId,
                        SUM(Debit) AS DebitBalance,
                        SUM(Credit) AS CreditBalance
                    FROM IssuedTafsiliLines
                    WHERE MoeenAccountId IS NOT NULL
                    GROUP BY MoeenAccountId
                ), TafsiliJournals AS (
                    SELECT DISTINCT JournalId
                    FROM IssuedTafsiliLines
                ), MoeenFromCounterAgg AS (
                    -- fallback فقط وقتی ParentAccountId خالی بوده یا ثبت نشده؛
                    -- از خطوط دیگر همان سند، معین‌ها را استخراج می‌کنیم
                    SELECT
                        CASE
                            WHEN a2.Level = 3 THEN a2.AccountId
                            WHEN a2.Level = 4 THEN a2.ParentAccountId
                            ELSE NULL
                        END AS MoeenAccountId,
                        SUM(jl2.Debit) AS DebitBalance,
                        SUM(jl2.Credit) AS CreditBalance
                    FROM dbo.JournalLines jl2
                    INNER JOIN TafsiliJournals tj ON tj.JournalId = jl2.JournalId
                    INNER JOIN dbo.Accounts a2 ON a2.AccountId = jl2.AccountId
                    WHERE a2.IsActive = 1
                        AND jl2.AccountId <> @tafsiliAccountId
                    GROUP BY
                        CASE
                            WHEN a2.Level = 3 THEN a2.AccountId
                            WHEN a2.Level = 4 THEN a2.ParentAccountId
                            ELSE NULL
                        END
                ), MoeenAgg AS (
                    SELECT MoeenAccountId, DebitBalance, CreditBalance
                    FROM MoeenFromParentAgg
                    UNION ALL
                    SELECT MoeenAccountId, DebitBalance, CreditBalance
                    FROM MoeenFromCounterAgg
                    WHERE NOT EXISTS (SELECT 1 FROM MoeenFromParentAgg)
                ), MoeenFinal AS (
                    SELECT
                        MoeenAccountId,
                        SUM(DebitBalance) AS DebitBalance,
                        SUM(CreditBalance) AS CreditBalance
                    FROM MoeenAgg
                    WHERE MoeenAccountId IS NOT NULL
                    GROUP BY MoeenAccountId
                )
                SELECT
                    a.AccountId,
                    a.Code,
                    a.Name,
                    a.Level,
                    a.AccountType,
                    a.ParentAccountId,
                    ISNULL(m.DebitBalance, 0) AS DebitBalance,
                    ISNULL(m.CreditBalance, 0) AS CreditBalance,
                    0 AS HasChildren
                FROM MoeenFinal m
                INNER JOIN dbo.Accounts a ON a.AccountId = m.MoeenAccountId
                WHERE a.IsActive = 1
                    AND a.Level = 3
                    AND (@showOnlyWithBalance = 0 OR ISNULL(m.DebitBalance, 0) <> ISNULL(m.CreditBalance, 0))
                ORDER BY a.Code";

            return await connection.QueryAsync<TrialBalanceNode>(sql, new { tafsiliAccountId, from, to, showOnlyWithBalance });
        }

    }

    public class TrialBalanceSummary
    {
        public decimal TotalDebits { get; set; }
        public decimal TotalCredits { get; set; }
        public int TotalAccounts { get; set; }
        public int TotalJournals { get; set; }
        
        public bool IsBalanced => TotalDebits == TotalCredits;
        public decimal Difference => TotalDebits - TotalCredits;
    }
}

