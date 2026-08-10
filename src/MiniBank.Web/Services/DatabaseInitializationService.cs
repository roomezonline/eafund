using Microsoft.Data.SqlClient;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MiniBank.Web.Services
{
    public class DatabaseInitializationService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseInitializationService> _logger;

        public DatabaseInitializationService(IConfiguration configuration, ILogger<DatabaseInitializationService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        /// <summary>
        /// اجرای اسکریپت‌های تکمیلی در پوشه database به ترتیب مورد نیاز
        /// </summary>
        public async Task ExecuteAdditionalScriptsAsync()
        {
            try
            {
                var baseDirs = new List<string>
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "database"),
                    Path.Combine(AppContext.BaseDirectory, "database"),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database"),
                };

                string? dbDir = baseDirs.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
                if (dbDir == null)
                {
                    _logger.LogWarning("پوشه database پیدا نشد برای اجرای اسکریپت‌های تکمیلی");
                    return;
                }

                // Ordered list to satisfy dependencies
                var files = new[]
                {
                    "transactions_categorization.sql",
                    "household_receipts.sql",
                    "transactions.sql",
                    "transactions_seed.sql",
                };

                foreach (var f in files)
                {
                    var path = Path.Combine(dbDir, f);
                    if (File.Exists(path))
                    {
                        await ExecuteSqlFileAsync(path);
                    }
                    else
                    {
                        _logger.LogInformation("فایل {File} یافت نشد و نادیده گرفته شد.", path);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در اجرای اسکریپت‌های تکمیلی پایگاه داده");
                throw;
            }
        }

        private async Task ExecuteSqlFileAsync(string filePath)
        {
            var scriptContent = await File.ReadAllTextAsync(filePath);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var batches = Regex.Split(scriptContent, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                               .Select(b => b.Trim())
                               .Where(b => !string.IsNullOrWhiteSpace(b))
                               .ToList();

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch) || batch.StartsWith("--"))
                    continue;

                using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("اسکریپت {File} با موفقیت اجرا شد.", Path.GetFileName(filePath));
        }

        /// <summary>
        /// بررسی و ایجاد خودکار جداول مورد نیاز
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            try
            {
                _logger.LogInformation("شروع بررسی و ایجاد جداول بانک اطلاعاتی...");

                // بررسی اتصال به بانک اطلاعاتی
                await TestConnectionAsync();

                // اجرای اسکریپت کلی schema.sql برای ساخت/به‌روزرسانی جداول و ستون‌ها (idempotent)
                await ExecuteSchemaScriptAsync();

                // ایجاد جدول خانوارها
                await EnsureHouseholdsTableAsync();

                // ایجاد جدول اعضا
                await EnsureMembersTableAsync();

                // ایجاد کلید خارجی OwnerMemberId (در صورت وجود ستون)
                await EnsureHouseholdsOwnerFkAsync();

                // ایجاد جدول حساب‌های بانکی صندوق
                await EnsureBankAccountsTableAsync();

                // ایجاد جداول نرخ سود سپرده (تک‌نرخی/پلکانی)
                await EnsureDepositRatePlansTableAsync();
                await EnsureDepositRateTiersTableAsync();

                _logger.LogInformation("بررسی و ایجاد جداول با موفقیت انجام شد.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ایجاد جداول بانک اطلاعاتی");
                throw;
            }
        }

        /// <summary>
        /// بررسی اتصال به بانک اطلاعاتی
        /// </summary>
        private async Task TestConnectionAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogInformation("اتصال به بانک اطلاعاتی برقرار شد.");
        }

        /// <summary>
        /// بررسی و ایجاد جدول خانوارها
        /// </summary>
        private async Task EnsureHouseholdsTableAsync()
        {
            const string checkTableSql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Households'";

            const string createTableSql = @"
                CREATE TABLE [dbo].[Households] (
                    [HouseholdId] BIGINT IDENTITY(1,1) NOT NULL,
                    [HouseholdCode] NVARCHAR(50) NOT NULL,
                    [Name] NVARCHAR(200) NOT NULL,
                    [OwnerMemberId] BIGINT NULL,
                    [IsActive] BIT NOT NULL DEFAULT(1),
                    [CreatedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
                    
                    CONSTRAINT [PK_Households] PRIMARY KEY CLUSTERED ([HouseholdId] ASC),
                    CONSTRAINT [UK_Households_HouseholdCode] UNIQUE NONCLUSTERED ([HouseholdCode] ASC)
                )";

            const string createIndexesSql = @"
                CREATE NONCLUSTERED INDEX [IX_Households_Name] ON [dbo].[Households] ([Name] ASC);
                CREATE NONCLUSTERED INDEX [IX_Households_IsActive] ON [dbo].[Households] ([IsActive] ASC);
                CREATE NONCLUSTERED INDEX [IX_Households_CreatedAt] ON [dbo].[Households] ([CreatedAt] DESC);";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // بررسی وجود جدول
            using var checkCommand = new SqlCommand(checkTableSql, connection);
            var tableExists = (int)await checkCommand.ExecuteScalarAsync() > 0;

            if (!tableExists)
            {
                _logger.LogInformation("ایجاد جدول Households...");

                // ایجاد جدول
                using var createCommand = new SqlCommand(createTableSql, connection);
                await createCommand.ExecuteNonQueryAsync();

                // ایجاد ایندکس‌ها
                using var indexCommand = new SqlCommand(createIndexesSql, connection);
                await indexCommand.ExecuteNonQueryAsync();

                _logger.LogInformation("جدول Households و ایندکس‌های آن ایجاد شد.");
            }
            else
            {
                _logger.LogInformation("جدول Households از قبل موجود است.");
                
                // بررسی و اعمال تغییرات ساختاری (در صورت نیاز)
                await CheckAndApplySchemaChangesAsync(connection);
            }
        }

        /// <summary>
        /// اضافه کردن داده‌های اولیه (فقط در صورت درخواست صریح)
        /// </summary>
        public async Task InsertInitialDataAsync(string tableName, string insertSql)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(insertSql, connection);
            await command.ExecuteNonQueryAsync();
            
            _logger.LogInformation("داده‌های اولیه جدول {TableName} اضافه شد.", tableName);
        }

        /// <summary>
        /// بررسی و اعمال تغییرات ساختاری جدول
        /// </summary>
        private async Task CheckAndApplySchemaChangesAsync(SqlConnection connection)
        {
            // در آینده می‌توان تغییرات ساختاری را اینجا پیاده‌سازی کرد
            // مثل اضافه کردن ستون جدید، تغییر نوع داده و غیره
            
            _logger.LogDebug("بررسی تغییرات ساختاری جدول Households انجام شد.");
        }

        /// <summary>
        /// بررسی و ایجاد جدول اعضا
        /// </summary>
        private async Task EnsureMembersTableAsync()
        {
            const string checkTableSql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Members'";

            const string createTableSql = @"
                CREATE TABLE [dbo].[Members] (
                    [MemberId] BIGINT IDENTITY(1,1) NOT NULL,
                    [FirstName] NVARCHAR(100) NOT NULL,
                    [FatherName] NVARCHAR(100) NULL,
                    [LastName] NVARCHAR(100) NOT NULL,
                    [NationalCode] NVARCHAR(10) NOT NULL,
                    [MobileNumber] NVARCHAR(15) NOT NULL,
                    [HouseholdId] BIGINT NOT NULL,
                    [IsActive] BIT NOT NULL DEFAULT(1),
                    [JoinedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
                    [CreatedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_Members] PRIMARY KEY CLUSTERED ([MemberId] ASC),
                    CONSTRAINT [UK_Members_NationalCode] UNIQUE NONCLUSTERED ([NationalCode] ASC),
                    CONSTRAINT [FK_Members_Households] FOREIGN KEY ([HouseholdId]) REFERENCES [dbo].[Households] ([HouseholdId])
                )";

            const string createIndexesSql = @"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_HouseholdId')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_Members_HouseholdId] ON [dbo].[Members] ([HouseholdId] ASC);
                END
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_Name')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_Members_Name] ON [dbo].[Members] ([FirstName] ASC, [LastName] ASC);
                END
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_IsActive')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_Members_IsActive] ON [dbo].[Members] ([IsActive] ASC);
                END
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Members]') AND name = N'IX_Members_JoinedAt')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_Members_JoinedAt] ON [dbo].[Members] ([JoinedAt] DESC);
                END";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // بررسی وجود جدول
            using (var checkCommand = new SqlCommand(checkTableSql, connection))
            {
                var tableExists = (int)await checkCommand.ExecuteScalarAsync() > 0;
                if (!tableExists)
                {
                    _logger.LogInformation("ایجاد جدول Members...");
                    using var createCommand = new SqlCommand(createTableSql, connection);
                    await createCommand.ExecuteNonQueryAsync();

                    using var indexCommand = new SqlCommand(createIndexesSql, connection);
                    await indexCommand.ExecuteNonQueryAsync();

                    _logger.LogInformation("جدول Members و ایندکس‌های آن ایجاد شد.");
                }
                else
                {
                    // اگر جدول وجود دارد، اطمینان از وجود ستون FatherName و ایندکس‌ها
                    const string addFatherNameIfMissing = @"IF COL_LENGTH('dbo.Members','FatherName') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [FatherName] NVARCHAR(100) NULL;
END";
                    using (var alterCmd = new SqlCommand(addFatherNameIfMissing, connection))
                    {
                        await alterCmd.ExecuteNonQueryAsync();
                    }

                    using var indexCommand = new SqlCommand(createIndexesSql, connection);
                    await indexCommand.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// بررسی و ایجاد جدول حساب‌های بانکی صندوق
        /// </summary>
        private async Task EnsureBankAccountsTableAsync()
        {
            const string checkTableSql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'BankAccounts'";

            const string createTableSql = @"
                CREATE TABLE [dbo].[BankAccounts] (
                    [AccountId] BIGINT IDENTITY(1,1) NOT NULL,
                    [BankName] NVARCHAR(200) NOT NULL,
                    [AccountNumber] NVARCHAR(50) NOT NULL,
                    [CardNumber] NVARCHAR(20) NULL,
                    [Shaba] NVARCHAR(26) NULL,
                    [AccountType] NVARCHAR(50) NULL,
                    [InterestRate] DECIMAL(5,2) NULL,
                    [IsActive] BIT NOT NULL DEFAULT(1),
                    [OpenedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
                    [CreatedAt] DATETIME2(7) NOT NULL DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_BankAccounts] PRIMARY KEY CLUSTERED ([AccountId] ASC)
                )";

            const string createIndexesSql = @"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_BankName')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_BankAccounts_BankName] ON [dbo].[BankAccounts] ([BankName] ASC);
                END
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_IsActive')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_BankAccounts_IsActive] ON [dbo].[BankAccounts] ([IsActive] ASC);
                END
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[BankAccounts]') AND name = N'IX_BankAccounts_CreatedAt')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_BankAccounts_CreatedAt] ON [dbo].[BankAccounts] ([CreatedAt] DESC);
                END";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var checkCommand = new SqlCommand(checkTableSql, connection);
            var tableExists = (int)await checkCommand.ExecuteScalarAsync() > 0;
            if (!tableExists)
            {
                _logger.LogInformation("ایجاد جدول BankAccounts...");
                using var createCommand = new SqlCommand(createTableSql, connection);
                await createCommand.ExecuteNonQueryAsync();
                using var indexCommand = new SqlCommand(createIndexesSql, connection);
                await indexCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("جدول BankAccounts و ایندکس‌های آن ایجاد شد.");
            }
            else
            {
                _logger.LogInformation("جدول BankAccounts از قبل موجود است.");
                using var indexCommand = new SqlCommand(createIndexesSql, connection);
                await indexCommand.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// اطمینان از وجود FK برای OwnerMemberId در جدول Households به جدول Members
        /// </summary>
        private async Task EnsureHouseholdsOwnerFkAsync()
        {
            const string checkFkSql = @"
                SELECT COUNT(*) FROM sys.foreign_keys 
                WHERE name = 'FK_Households_Members_Owner' AND parent_object_id = OBJECT_ID(N'[dbo].[Households]')";

            const string addFkSql = @"
                IF COL_LENGTH('dbo.Households', 'OwnerMemberId') IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Households_Members_Owner')
                BEGIN
                    ALTER TABLE [dbo].[Households]
                    ADD CONSTRAINT [FK_Households_Members_Owner] FOREIGN KEY ([OwnerMemberId]) REFERENCES [dbo].[Members] ([MemberId]);
                END";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = new SqlCommand(addFkSql, connection);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// اجرای اسکریپت SQL از فایل
        /// </summary>
        public async Task ExecuteSchemaScriptAsync()
        {
            try
            {
                string? schemaPath = null;
                var candidates = new List<string>
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "database", "schema.sql"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "database", "schema.sql"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "database", "schema.sql"),
                    Path.Combine(AppContext.BaseDirectory, "database", "schema.sql"),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "schema.sql"),
                };
                foreach (var p in candidates)
                {
                    var full = Path.GetFullPath(p);
                    if (File.Exists(full)) { schemaPath = full; break; }
                }
                if (schemaPath == null)
                {
                    _logger.LogWarning("فایل schema.sql پیدا نشد. مسیرهای بررسی‌شده: {Paths}", string.Join(" | ", candidates.Select(Path.GetFullPath)));
                    return;
                }
                else
                {
                    _logger.LogInformation("اجرای اسکریپت پایگاه داده از مسیر: {Path}", schemaPath);
                }

                var scriptContent = await File.ReadAllTextAsync(schemaPath);
                
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // تقسیم اسکریپت به دستورات جداگانه (بر اساس GO در ابتدای خط)
                var batches = Regex.Split(scriptContent, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                                   .Select(b => b.Trim())
                                   .Where(b => !string.IsNullOrWhiteSpace(b))
                                   .ToList();

                foreach (var batch in batches)
                {
                    var trimmedBatch = batch.Trim();
                    if (string.IsNullOrEmpty(trimmedBatch) || trimmedBatch.StartsWith("--"))
                        continue;

                    using var command = new SqlCommand(trimmedBatch, connection);
                    await command.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("اسکریپت schema.sql با موفقیت اجرا شد.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در اجرای اسکریپت schema.sql");
                throw;
            }
        }

        /// <summary>
        /// اطمینان از آماده بودن جدول اعضا و کلیدهای مرتبط (برای فراخوانی در لود صفحات)
        /// </summary>
        public async Task EnsureMembersReadyAsync()
        {
            // اتصال تست و ساخت جدول ها در صورت نیاز
            await TestConnectionAsync();
            await ExecuteSchemaScriptAsync();
            await EnsureHouseholdsTableAsync();
            await EnsureMembersTableAsync();
            await EnsureHouseholdsOwnerFkAsync();
            await EnsureBankAccountsTableAsync();
            await EnsureDepositRatePlansTableAsync();
            await EnsureDepositRateTiersTableAsync();
        }

        private async Task EnsureDepositRatePlansTableAsync()
        {
            const string checkSql = @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='DepositRatePlans'";
            const string createSql = @"
                CREATE TABLE [dbo].[DepositRatePlans] (
                    [PlanId] BIGINT IDENTITY(1,1) NOT NULL,
                    [Mode] TINYINT NOT NULL,
                    [EffectiveFrom] DATE NOT NULL,
                    [EffectiveTo] DATE NULL,
                    [SingleRatePercent] DECIMAL(5,2) NULL,
                    [Notes] NVARCHAR(400) NULL,
                    [IsActive] BIT NOT NULL CONSTRAINT DF_DepositRatePlans_IsActive DEFAULT(1),
                    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_DepositRatePlans_CreatedAt DEFAULT(GETUTCDATE()),
                    CONSTRAINT [PK_DepositRatePlans] PRIMARY KEY CLUSTERED ([PlanId] ASC)
                );";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var check = new SqlCommand(checkSql, connection);
            var exists = (int)await check.ExecuteScalarAsync() > 0;
            if (!exists)
            {
                using var create = new SqlCommand(createSql, connection);
                await create.ExecuteNonQueryAsync();
                _logger.LogInformation("جدول DepositRatePlans ایجاد شد");
            }
        }

        private async Task EnsureDepositRateTiersTableAsync()
        {
            const string checkSql = @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='DepositRateTiers'";
            const string createSql = @"
                CREATE TABLE [dbo].[DepositRateTiers] (
                    [TierId] BIGINT IDENTITY(1,1) NOT NULL,
                    [PlanId] BIGINT NOT NULL,
                    [OrderNo] INT NOT NULL,
                    [FromAmount] DECIMAL(18,2) NOT NULL,
                    [ToAmount] DECIMAL(18,2) NULL,
                    [RatePercent] DECIMAL(5,2) NOT NULL,
                    CONSTRAINT [PK_DepositRateTiers] PRIMARY KEY CLUSTERED ([TierId] ASC),
                    CONSTRAINT [FK_DepositRateTiers_DepositRatePlans] FOREIGN KEY ([PlanId]) REFERENCES [dbo].[DepositRatePlans]([PlanId]) ON DELETE CASCADE
                );
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[DepositRateTiers]') AND name = N'IX_DepositRateTiers_PlanId')
                BEGIN
                    CREATE NONCLUSTERED INDEX [IX_DepositRateTiers_PlanId] ON [dbo].[DepositRateTiers] ([PlanId] ASC);
                END";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var check = new SqlCommand(checkSql, connection);
            var exists = (int)await check.ExecuteScalarAsync() > 0;
            if (!exists)
            {
                using var create = new SqlCommand(createSql, connection);
                await create.ExecuteNonQueryAsync();
                _logger.LogInformation("جدول DepositRateTiers ایجاد شد");
            }
        }
    }
}
