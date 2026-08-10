using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models.Admin;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace MiniBank.Web.Services
{
    public sealed class DbBackupService
    {
        private readonly IConfiguration _config;
        private readonly DbBackupRepository _repo;
        private readonly ILogger<DbBackupService> _logger;

        public DbBackupService(IConfiguration config, DbBackupRepository repo, ILogger<DbBackupService> logger)
        {
            _config = config;
            _repo = repo;
            _logger = logger;
        }

        public Task<DbBackupSettings> GetSettingsAsync() => _repo.GetSettingsAsync();
        public Task SaveSettingsAsync(DbBackupSettings s) => _repo.UpsertSettingsAsync(s);
        public Task<IReadOnlyList<DbBackupRun>> GetRunsAsync(int take = 50) => _repo.GetRunsAsync(take);

        public async Task<DbBackupRun?> GetRunByIdAsync(long runId)
        {
            return await _repo.GetRunByIdAsync(runId);
        }

        public async Task<(bool ok, string? fullPath, string? downloadName, string? error)> TryGetDownloadInfoAsync(long runId)
        {
            try
            {
                var settings = await _repo.GetSettingsAsync();
                if (string.IsNullOrWhiteSpace(settings.BackupDirectory))
                    return (false, null, null, "مسیر بکاپ تنظیم نشده است.");

                var run = await _repo.GetRunByIdAsync(runId);
                if (run is null)
                    return (false, null, null, "رکورد یافت نشد.");

                if (string.IsNullOrWhiteSpace(run.BackupFilePath))
                    return (false, null, null, "فایل برای این رکورد ثبت نشده است.");

                if (!TryResolveSafeBackupFile(settings.BackupDirectory, run.BackupFilePath, out var fullPath))
                    return (false, null, null, "مسیر فایل معتبر نیست.");

                if (!File.Exists(fullPath))
                    return (false, null, null, "فایل روی سرور موجود نیست.");

                var dn = Path.GetFileName(fullPath);
                return (true, fullPath, dn, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TryGetDownloadInfo failed");
                return (false, null, null, ex.Message);
            }
        }

        public async Task<(bool ok, string? error)> DeleteRunAndFileAsync(long runId)
        {
            try
            {
                var settings = await _repo.GetSettingsAsync();
                var run = await _repo.GetRunByIdAsync(runId);
                if (run is null)
                    return (false, "رکورد یافت نشد.");

                // Delete file (best-effort)
                if (!string.IsNullOrWhiteSpace(settings.BackupDirectory) && !string.IsNullOrWhiteSpace(run.BackupFilePath))
                {
                    if (TryResolveSafeBackupFile(settings.BackupDirectory, run.BackupFilePath, out var fullPath))
                    {
                        try
                        {
                            if (File.Exists(fullPath))
                                File.Delete(fullPath);
                        }
                        catch { }
                    }
                }

                await _repo.DeleteRunAsync(runId);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteRunAndFile failed");
                return (false, ex.Message);
            }
        }

        public async Task<(bool ok, string message)> TestBackupDirectoryAccessAsync(string? backupDirectory, string? databaseName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(backupDirectory))
                    return (false, "مسیر پوشه بکاپ مشخص نشده است.");

                var cs = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string not found");
                var dbName = string.IsNullOrWhiteSpace(databaseName) ? ResolveDatabaseNameFromConnectionString(cs) : databaseName;
                if (string.IsNullOrWhiteSpace(dbName))
                    return (false, "نام دیتابیس مشخص نشده است.");

                var testFileName = $"{dbName}_PERMTEST_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                var testPath = Path.Combine(backupDirectory, testFileName);

                await using var conn = new SqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteBackupDatabaseAsync(conn, dbName, testPath, copyOnly: true, stats: 1);

                return (true, $"دسترسی نوشتن تایید شد. فایل تست ساخته شد: {testPath}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool ok, long runId, string? filePath, string? error)> RunBackupNowAsync(string triggeredBy)
        {
            var settings = await _repo.GetSettingsAsync();
            var startedAtUtc = DateTime.UtcNow;
            var runId = await _repo.CreateRunAsync(startedAtUtc, status: "running");

            string? bakPath = null;
            long? size = null;
            string? error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(settings.BackupDirectory))
                    throw new InvalidOperationException("مسیر پوشه بکاپ مشخص نشده است.");

                var cs = _config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string not found");
                var dbName = string.IsNullOrWhiteSpace(settings.DatabaseName)
                    ? ResolveDatabaseNameFromConnectionString(cs)
                    : settings.DatabaseName;
                if (string.IsNullOrWhiteSpace(dbName))
                    throw new InvalidOperationException("نام دیتابیس مشخص نشده است.");

                Directory.CreateDirectory(settings.BackupDirectory);

                var fileName = $"{dbName}_FULL_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                bakPath = Path.Combine(settings.BackupDirectory, fileName);
                await using var conn = new SqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteBackupDatabaseAsync(conn, dbName, bakPath, copyOnly: false, stats: 10);

                string? verifyWarning = null;
                try
                {
                    var verifySql = "RESTORE VERIFYONLY FROM DISK = @p0 WITH CHECKSUM;";
                    await using var cmd = new SqlCommand(verifySql, conn);
                    cmd.CommandTimeout = 60 * 60;
                    cmd.Parameters.AddWithValue("@p0", bakPath);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (SqlException ex) when (IsVerifyPermissionDenied(ex))
                {
                    verifyWarning = "هشدار: بررسی صحت فایل (VERIFY) به دلیل محدودیت دسترسی روی سرور انجام نشد. فایل بکاپ ساخته شد.";
                }

                var fi = new FileInfo(bakPath);
                size = fi.Exists ? fi.Length : (long?)null;

                await EnforceRetentionAsync(settings.BackupDirectory, dbName, settings.RetainLastCount <= 0 ? 2 : settings.RetainLastCount);

                await TrySendEmailAsync(settings, runId, bakPath, size, triggeredBy);

                await _repo.MarkRunResultAsync(runId, DateTime.UtcNow, "success", bakPath, size, verifyWarning);
                return (true, runId, bakPath, null);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogError(ex, "Db backup failed");
                await _repo.MarkRunResultAsync(runId, DateTime.UtcNow, "failed", bakPath, size, error);
                return (false, runId, bakPath, error);
            }
        }

        private static async Task ExecuteBackupDatabaseAsync(SqlConnection conn, string dbName, string filePath, bool copyOnly, int stats)
        {
            var escapedDbName = dbName.Replace("]", "]]" );

            try
            {
                await ExecuteBackupDatabaseInternalAsync(conn, escapedDbName, filePath, copyOnly, stats, useCompression: true);
            }
            catch (SqlException ex) when (IsCompressionNotSupported(ex))
            {
                await ExecuteBackupDatabaseInternalAsync(conn, escapedDbName, filePath, copyOnly, stats, useCompression: false);
            }
        }

        private static async Task ExecuteBackupDatabaseInternalAsync(SqlConnection conn, string escapedDbName, string filePath, bool copyOnly, int stats, bool useCompression)
        {
            var withParts = new List<string>();
            if (copyOnly) withParts.Add("COPY_ONLY");
            withParts.Add("INIT");
            withParts.Add("CHECKSUM");
            if (useCompression) withParts.Add("COMPRESSION");
            withParts.Add($"STATS={stats}");

            var sql = $@"
DECLARE @file nvarchar(4000) = @p0;
BACKUP DATABASE [{escapedDbName}]
TO DISK = @file
WITH {string.Join(", ", withParts)};";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 60 * 60;
            cmd.Parameters.AddWithValue("@p0", filePath);
            await cmd.ExecuteNonQueryAsync();
        }

        private static bool IsCompressionNotSupported(SqlException ex)
        {
            var msg = ex.Message ?? string.Empty;
            return msg.Contains("not supported on Express Edition", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("COMPRESSION", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVerifyPermissionDenied(SqlException ex)
        {
            var msg = ex.Message ?? string.Empty;
            return ex.Number == 262
                || msg.Contains("CREATE DATABASE permission denied", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("VERIFY DATABASE", StringComparison.OrdinalIgnoreCase);
        }

        private static Task EnforceRetentionAsync(string dir, string dbName, int keep)
        {
            try
            {
                if (keep < 1) keep = 1;
                var di = new DirectoryInfo(dir);
                if (!di.Exists) return Task.CompletedTask;

                var files = di.GetFiles($"{dbName}_FULL_*.bak", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                foreach (var f in files.Skip(keep))
                {
                    try { f.Delete(); } catch { }
                }
            }
            catch { }
            return Task.CompletedTask;
        }

        private async Task TrySendEmailAsync(DbBackupSettings s, long runId, string bakPath, long? size, string triggeredBy)
        {
            var toList = (s.NotifyEmailsCsv ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (toList.Count == 0) return;
            if (string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.SmtpUsername) || 
                string.IsNullOrWhiteSpace(s.SmtpPassword) || string.IsNullOrWhiteSpace(s.SmtpFrom))
                return;

            var subject = $"پشتیبان بانک اطلاعاتی مینی بانک - {DateTime.Now:yyyy/MM/dd HH:mm}";
            var body = new StringBuilder();
            body.AppendLine("نتیجه پشتیبان‌گیری: موفق");
            body.AppendLine($"شناسه عملیات: {runId}");
            body.AppendLine($"توسط: {triggeredBy}");
            body.AppendLine($"مسیر فایل: {bakPath}");
            body.AppendLine($"حجم: {(size.HasValue ? (size.Value / 1024 / 1024) + " مگابایت" : "-")}");

            using var msg = new MailMessage();
            msg.From = new MailAddress(s.SmtpFrom);
            foreach (var t in toList) msg.To.Add(t);
            msg.Subject = subject;
            msg.Body = body.ToString();
            msg.IsBodyHtml = false;

            // Always try to attach the backup file if it exists
            bool attachmentAdded = false;
            if (File.Exists(bakPath))
            {
                try 
                { 
                    var attachment = new Attachment(bakPath);
                    msg.Attachments.Add(attachment);
                    attachmentAdded = true;
                    body.AppendLine($"\nفایل پشتیبان به پیوست ایمیل اضافه شد.");
                } 
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در پیوست فایل پشتیبان به ایمیل");
                    body.AppendLine($"\nخطا در پیوست فایل پشتیبان: {ex.Message}");
                }
            }
            else
            {
                body.AppendLine("\nهشدار: فایل پشتیبان یافت نشد.");
            }

            try
            {
                using var client = new SmtpClient(s.SmtpHost, s.SmtpPort)
                {
                    EnableSsl = s.SmtpUseSsl,
                    Credentials = new NetworkCredential(s.SmtpUsername, s.SmtpPassword)
                };

                await client.SendMailAsync(msg);
                _logger.LogInformation("ایمیل اطلاع‌رسانی پشتیبان با {AttachmentStatus} ارسال شد", 
                    attachmentAdded ? "پیوست" : "بدون پیوست");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در ارسال ایمیل اطلاع‌رسانی پشتیبان");
                throw; // Rethrow to mark the backup as failed
            }
        }

        private static string? ResolveDatabaseNameFromConnectionString(string connectionString)
        {
            try
            {
                var b = new SqlConnectionStringBuilder(connectionString);
                return string.IsNullOrWhiteSpace(b.InitialCatalog) ? null : b.InitialCatalog;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryResolveSafeBackupFile(string backupDirectory, string storedPath, out string fullPath)
        {
            fullPath = string.Empty;
            try
            {
                var baseDir = Path.GetFullPath(backupDirectory);
                var candidate = Path.IsPathRooted(storedPath)
                    ? Path.GetFullPath(storedPath)
                    : Path.GetFullPath(Path.Combine(baseDir, storedPath));

                if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
                    baseDir += Path.DirectorySeparatorChar;

                if (!candidate.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    return false;

                fullPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
