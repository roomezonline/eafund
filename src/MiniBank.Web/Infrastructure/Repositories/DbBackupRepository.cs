using Dapper;
using MiniBank.Web.Models.Admin;
using System.Data;
using System.Data.Common;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public sealed class DbBackupRepository
    {
        private readonly IDbConnection _db;

        private const string SchedulerLockResource = "MiniBank.DbBackupScheduler";

        public DbBackupRepository(IDbConnection db)
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

        public async Task EnsureSchemaAsync()
        {
            await EnsureOpenAsync();
            const string sql = @"
IF OBJECT_ID(N'dbo.DbBackupSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DbBackupSettings (
        SettingsId INT NOT NULL CONSTRAINT PK_DbBackupSettings PRIMARY KEY,
        IsEnabled BIT NOT NULL CONSTRAINT DF_DbBackupSettings_IsEnabled DEFAULT(0),
        DatabaseName NVARCHAR(200) NULL,
        BackupDirectory NVARCHAR(4000) NULL,
        RunAtHourLocal INT NOT NULL CONSTRAINT DF_DbBackupSettings_RunAtHourLocal DEFAULT(2),
        Frequency INT NOT NULL CONSTRAINT DF_DbBackupSettings_Frequency DEFAULT(1),
        RunAtTimeLocalMinutes INT NOT NULL CONSTRAINT DF_DbBackupSettings_RunAtTimeLocalMinutes DEFAULT(120),
        RunOnDayOfWeek INT NULL,
        RunOnDayOfMonth INT NULL,
        RetainLastCount INT NOT NULL CONSTRAINT DF_DbBackupSettings_RetainLastCount DEFAULT(2),
        NotifyEmailsCsv NVARCHAR(2000) NULL,
        SmtpHost NVARCHAR(500) NULL,
        SmtpPort INT NOT NULL CONSTRAINT DF_DbBackupSettings_SmtpPort DEFAULT(587),
        SmtpUseSsl BIT NOT NULL CONSTRAINT DF_DbBackupSettings_SmtpUseSsl DEFAULT(1),
        SmtpUsername NVARCHAR(500) NULL,
        SmtpPassword NVARCHAR(500) NULL,
        SmtpFrom NVARCHAR(500) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_DbBackupSettings_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_DbBackupSettings_UpdatedAt DEFAULT(SYSDATETIME())
    );
    INSERT INTO dbo.DbBackupSettings (SettingsId, IsEnabled) VALUES (1, 0);
END

IF COL_LENGTH('dbo.DbBackupSettings', 'Frequency') IS NULL
    ALTER TABLE dbo.DbBackupSettings ADD Frequency INT NOT NULL CONSTRAINT DF_DbBackupSettings_Frequency_2 DEFAULT(1);
IF COL_LENGTH('dbo.DbBackupSettings', 'RunAtTimeLocalMinutes') IS NULL
    ALTER TABLE dbo.DbBackupSettings ADD RunAtTimeLocalMinutes INT NOT NULL CONSTRAINT DF_DbBackupSettings_RunAtTimeLocalMinutes_2 DEFAULT(120);
IF COL_LENGTH('dbo.DbBackupSettings', 'RunOnDayOfWeek') IS NULL
    ALTER TABLE dbo.DbBackupSettings ADD RunOnDayOfWeek INT NULL;
IF COL_LENGTH('dbo.DbBackupSettings', 'RunOnDayOfMonth') IS NULL
    ALTER TABLE dbo.DbBackupSettings ADD RunOnDayOfMonth INT NULL;

IF OBJECT_ID(N'dbo.DbBackupRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DbBackupRuns (
        RunId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DbBackupRuns PRIMARY KEY,
        StartedAtUtc DATETIME2(7) NOT NULL,
        FinishedAtUtc DATETIME2(7) NULL,
        Status NVARCHAR(40) NOT NULL,
        BackupFilePath NVARCHAR(4000) NULL,
        BackupFileSizeBytes BIGINT NULL,
        ErrorMessage NVARCHAR(2000) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_DbBackupRuns_CreatedAtUtc DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_DbBackupRuns_CreatedAtUtc ON dbo.DbBackupRuns(CreatedAtUtc DESC);
END
;

-- Ensure default constraints use server local time
IF OBJECT_ID(N'dbo.DbBackupSettings', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'DF_DbBackupSettings_CreatedAt', N'D') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.DbBackupSettings DROP CONSTRAINT DF_DbBackupSettings_CreatedAt;
        ALTER TABLE dbo.DbBackupSettings ADD CONSTRAINT DF_DbBackupSettings_CreatedAt DEFAULT(SYSDATETIME()) FOR CreatedAt;
    END
    IF OBJECT_ID(N'DF_DbBackupSettings_UpdatedAt', N'D') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.DbBackupSettings DROP CONSTRAINT DF_DbBackupSettings_UpdatedAt;
        ALTER TABLE dbo.DbBackupSettings ADD CONSTRAINT DF_DbBackupSettings_UpdatedAt DEFAULT(SYSDATETIME()) FOR UpdatedAt;
    END
END

IF OBJECT_ID(N'dbo.DbBackupRuns', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'DF_DbBackupRuns_CreatedAtUtc', N'D') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.DbBackupRuns DROP CONSTRAINT DF_DbBackupRuns_CreatedAtUtc;
        ALTER TABLE dbo.DbBackupRuns ADD CONSTRAINT DF_DbBackupRuns_CreatedAtUtc DEFAULT(SYSUTCDATETIME()) FOR CreatedAtUtc;
    END
END
";
            await _db.ExecuteAsync(sql);
        }

        public async Task<DbBackupSettings> GetSettingsAsync()
        {
            await EnsureSchemaAsync();
            const string sql = @"SELECT TOP 1 SettingsId, IsEnabled, DatabaseName, BackupDirectory, Frequency, RunAtTimeLocalMinutes, RunOnDayOfWeek, RunOnDayOfMonth, RetainLastCount, NotifyEmailsCsv, SmtpHost, SmtpPort, SmtpUseSsl, SmtpUsername, SmtpPassword, SmtpFrom, CreatedAt, UpdatedAt FROM dbo.DbBackupSettings WHERE SettingsId = 1";
            var row = await _db.QueryFirstOrDefaultAsync<DbBackupSettings>(sql);
            return row ?? new DbBackupSettings { SettingsId = 1, IsEnabled = false };
        }

        public async Task UpsertSettingsAsync(DbBackupSettings s)
        {
            await EnsureSchemaAsync();
            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.DbBackupSettings WHERE SettingsId = 1)
BEGIN
    UPDATE dbo.DbBackupSettings
    SET IsEnabled = @IsEnabled,
        DatabaseName = @DatabaseName,
        BackupDirectory = @BackupDirectory,
        Frequency = @Frequency,
        RunAtTimeLocalMinutes = @RunAtTimeLocalMinutes,
        RunOnDayOfWeek = @RunOnDayOfWeek,
        RunOnDayOfMonth = @RunOnDayOfMonth,
        RetainLastCount = @RetainLastCount,
        NotifyEmailsCsv = @NotifyEmailsCsv,
        SmtpHost = @SmtpHost,
        SmtpPort = @SmtpPort,
        SmtpUseSsl = @SmtpUseSsl,
        SmtpUsername = @SmtpUsername,
        SmtpPassword = @SmtpPassword,
        SmtpFrom = @SmtpFrom,
        UpdatedAt = SYSDATETIME()
    WHERE SettingsId = 1;
END
ELSE
BEGIN
    INSERT INTO dbo.DbBackupSettings (SettingsId, IsEnabled, DatabaseName, BackupDirectory, Frequency, RunAtTimeLocalMinutes, RunOnDayOfWeek, RunOnDayOfMonth, RetainLastCount, NotifyEmailsCsv, SmtpHost, SmtpPort, SmtpUseSsl, SmtpUsername, SmtpPassword, SmtpFrom)
    VALUES (1, @IsEnabled, @DatabaseName, @BackupDirectory, @Frequency, @RunAtTimeLocalMinutes, @RunOnDayOfWeek, @RunOnDayOfMonth, @RetainLastCount, @NotifyEmailsCsv, @SmtpHost, @SmtpPort, @SmtpUseSsl, @SmtpUsername, @SmtpPassword, @SmtpFrom);
END
";
            await _db.ExecuteAsync(sql, s);
        }

        public async Task<long> CreateRunAsync(DateTime startedAtUtc, string status)
        {
            await EnsureSchemaAsync();
            const string sql = @"INSERT INTO dbo.DbBackupRuns(StartedAtUtc, Status) OUTPUT INSERTED.RunId VALUES (@startedAtUtc, @status)";
            return await _db.ExecuteScalarAsync<long>(sql, new { startedAtUtc, status });
        }

        public async Task MarkRunResultAsync(long runId, DateTime? finishedAtUtc, string status, string? backupFilePath, long? backupFileSizeBytes, string? errorMessage)
        {
            await EnsureSchemaAsync();
            const string sql = @"
UPDATE dbo.DbBackupRuns
SET FinishedAtUtc = @finishedAtUtc,
    Status = @status,
    BackupFilePath = @backupFilePath,
    BackupFileSizeBytes = @backupFileSizeBytes,
    ErrorMessage = @errorMessage
WHERE RunId = @runId";
            await _db.ExecuteAsync(sql, new { runId, finishedAtUtc, status, backupFilePath, backupFileSizeBytes, errorMessage });
        }

        public async Task<DbBackupRun?> GetRunByIdAsync(long runId)
        {
            await EnsureSchemaAsync();
            const string sql = @"SELECT TOP 1 RunId, StartedAtUtc, FinishedAtUtc, Status, BackupFilePath, BackupFileSizeBytes AS BackupFileSizeBytes, ErrorMessage, CreatedAtUtc FROM dbo.DbBackupRuns WHERE RunId = @runId";
            return await _db.QueryFirstOrDefaultAsync<DbBackupRun>(sql, new { runId });
        }

        public async Task DeleteRunAsync(long runId)
        {
            await EnsureSchemaAsync();
            const string sql = @"DELETE FROM dbo.DbBackupRuns WHERE RunId = @runId";
            await _db.ExecuteAsync(sql, new { runId });
        }

        public async Task<IReadOnlyList<DbBackupRun>> GetRunsAsync(int take = 50)
        {
            await EnsureSchemaAsync();
            const string sql = @"SELECT TOP (@take) RunId, StartedAtUtc, FinishedAtUtc, Status, BackupFilePath, BackupFileSizeBytes AS BackupFileSizeBytes, ErrorMessage, CreatedAtUtc FROM dbo.DbBackupRuns ORDER BY CreatedAtUtc DESC";
            var rows = await _db.QueryAsync<DbBackupRun>(sql, new { take });
            return rows.ToList();
        }

        public async Task<DateTime?> GetLastSuccessfulRunUtcAsync()
        {
            await EnsureSchemaAsync();
            const string sql = @"SELECT TOP 1 StartedAtUtc FROM dbo.DbBackupRuns WHERE Status = N'success' ORDER BY StartedAtUtc DESC";
            return await _db.ExecuteScalarAsync<DateTime?>(sql);
        }

        public async Task<bool> TryAcquireSchedulerLockAsync(int timeoutMs = 0)
        {
            await EnsureOpenAsync();
            const string sql = @"
DECLARE @res INT;
EXEC @res = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout;
SELECT @res;
";
            var res = await _db.ExecuteScalarAsync<int>(sql, new { resource = SchedulerLockResource, timeout = timeoutMs });
            return res >= 0;
        }

        public async Task ReleaseSchedulerLockAsync()
        {
            await EnsureOpenAsync();
            const string sql = @"EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
            try { await _db.ExecuteAsync(sql, new { resource = SchedulerLockResource }); } catch { }
        }
    }
}
