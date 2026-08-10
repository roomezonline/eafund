using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniBank.Web.Services
{
    /// <summary>
    /// Centralized migration runner that scans the 'database' folder for .sql files,
    /// orders them by filename, and applies batches split by GO. It stores an entry
    /// per file+hash in dbo.__SchemaMigrations to avoid re-running unchanged scripts.
    /// Scripts should be idempotent.
    /// </summary>
    public class DatabaseMigrationService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseMigrationService> _logger;
        private readonly IWebHostEnvironment _env;

        public DatabaseMigrationService(IConfiguration configuration, ILogger<DatabaseMigrationService> logger, IWebHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _env = env;
        }

        public async Task ApplyMigrationsAsync()
        {
            _logger.LogInformation("🔗 Testing database connection...");
            await EnsureConnectionAsync();
            _logger.LogInformation("✅ Database connection successful");
            
            _logger.LogInformation("📋 Ensuring migrations table exists...");
            await EnsureMigrationsTableAsync();
            _logger.LogInformation("✅ Migrations table ready");

            _logger.LogInformation("📁 Searching for database directory...");
            var dbDir = ResolveDatabaseDirectory();
            if (dbDir is null)
            {
                _logger.LogError("❌ Database directory not found. Searched relative to: CurrentDir={CurrentDir}, ContentRoot={ContentRoot}, BaseDir={BaseDir}",
                    Directory.GetCurrentDirectory(), _env?.ContentRootPath, AppContext.BaseDirectory);
                throw new DirectoryNotFoundException("database folder not found. See logs for searched locations.");
            }
            _logger.LogInformation("✅ Database directory found: {DatabaseDir}", dbDir);

            var allFiles = Directory.GetFiles(dbDir, "*.sql", SearchOption.TopDirectoryOnly);
            var schemaFile = allFiles.FirstOrDefault(f => string.Equals(Path.GetFileName(f), "schema.sql", StringComparison.OrdinalIgnoreCase));

            List<string> sqlFiles;
            if (schemaFile != null)
            {
                // User prefers a single unified schema.sql – execute only this file
                sqlFiles = new List<string> { schemaFile };
                _logger.LogInformation("📄 Found schema.sql - executing unified schema file only");
                _logger.LogInformation("📂 Schema file path: {SchemaFile}", schemaFile);
            }
            else
            {
                // Fallback: Only include files with numeric prefix to control order, e.g., 001_add_level_column.sql
                sqlFiles = allFiles
                    .Where(f => Regex.IsMatch(Path.GetFileName(f) ?? string.Empty, @"^\d{3}_.+\.sql$", RegexOptions.IgnoreCase))
                    .OrderBy(f => ComputeOrderKey(Path.GetFileName(f)))
                    .ThenBy(Path.GetFileName)
                    .ToList();
                var ignored = allFiles.Length - sqlFiles.Count;
                _logger.LogInformation("📊 Migration files analysis: Total={Total}, Will execute={Execute}, Ignored={Ignored}", allFiles.Length, sqlFiles.Count, ignored);
                if (sqlFiles.Any())
                {
                    _logger.LogInformation("📋 Execution order: {Files}", string.Join(", ", sqlFiles.Select(Path.GetFileName)));
                }
            }
            if (sqlFiles.Count == 0)
                throw new FileNotFoundException($"No .sql files found under {dbDir}");

            // Multi-pass dependency-resilient application
            var pending = new List<(string path, string name, string content, string hash)>();
            foreach (var file in sqlFiles)
            {
                var name = Path.GetFileName(file);
                var content = await File.ReadAllTextAsync(file);
                var hash = ComputeHash(content);
                if (await IsAlreadyAppliedAsync(name, hash))
                {
                    _logger.LogInformation("⏭️ Skipping {File} - already applied with same hash", name);
                    continue;
                }
                _logger.LogInformation("📝 Queued for execution: {File} (hash: {Hash})", name, hash[..8] + "...");
                pending.Add((file, name, content, hash));
            }

            int pass = 0;
            const int maxPasses = 8;
            if (pending.Count == 0)
            {
                _logger.LogInformation("✅ All migrations already applied - no changes needed");
                return;
            }
            
            _logger.LogInformation("🔄 Starting migration execution - {Count} files to process", pending.Count);
            
            while (pending.Count > 0 && pass < maxPasses)
            {
                pass++;
                _logger.LogInformation("🔄 Migration pass {Pass}/{MaxPasses} - processing {Count} files", pass, maxPasses, pending.Count);
                var appliedThisPass = new List<(string path, string name)>();

                foreach (var item in pending)
                {
                    _logger.LogInformation("⚡ Executing migration: {File}", item.name);
                    var migrationStartTime = DateTime.Now;
                    try
                    {
                        // Run batches with internal dependency deferral to allow partial progress within a single file
                        await ExecuteSqlBatchesResilientAsync(item.content, item.name);
                        await InsertMigrationRowAsync(item.name, item.hash);
                        var duration = DateTime.Now - migrationStartTime;
                        _logger.LogInformation("✅ Successfully applied {File} in {Duration}ms", item.name, duration.TotalMilliseconds);
                        appliedThisPass.Add((item.path, item.name));
                    }
                    catch (SqlException sqlEx)
                    {
                        // If dependency missing, defer to next pass
                        var msg = sqlEx.Message.ToLowerInvariant();
                        if (msg.Contains("does not exist") || msg.Contains("invalid object name") || msg.Contains("references") || msg.Contains("dependent") )
                        {
                            var duration = DateTime.Now - migrationStartTime;
                            _logger.LogWarning("⏸️ Deferring {File} (dependency not ready after {Duration}ms): {Error}", item.name, duration.TotalMilliseconds, sqlEx.Message);
                            continue;
                        }
                        _logger.LogError(sqlEx, "SQL error applying migration {File}", item.name);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error applying migration {File}", item.name);
                        throw;
                    }
                }

                if (appliedThisPass.Count == 0)
                {
                    var pendingNames = string.Join(", ", pending.Select(p => p.name));
                    _logger.LogError("❌ No progress in migration pass {Pass}. Stuck files: {PendingFiles}", pass, pendingNames);
                    throw new InvalidOperationException($"No progress in migration pass {pass}. Pending files: {pendingNames}");
                }
                
                _logger.LogInformation("✅ Pass {Pass} completed - applied {Count} files: {AppliedFiles}", pass, appliedThisPass.Count, string.Join(", ", appliedThisPass.Select(a => a.name)));

                // remove applied from pending
                pending.RemoveAll(p => appliedThisPass.Any(a => a.name == p.name));
            }

            if (pending.Count > 0)
            {
                var pendingNames = string.Join(", ", pending.Select(p => p.name));
                _logger.LogError("❌ Migration incomplete after {MaxPasses} passes. Remaining files: {PendingFiles}", maxPasses, pendingNames);
                throw new InvalidOperationException($"Migration incomplete. Files still pending after {maxPasses} passes: {pendingNames}");
            }
            
            _logger.LogInformation("🎉 All migrations completed successfully!");
        }

        private async Task EnsureConnectionAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            // Ensure recommended session options for creating indexes/views etc.
            using (var pre = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", connection))
            {
                await pre.ExecuteNonQueryAsync();
            }
        }

        private string? ResolveDatabaseDirectory()
        {
            var cr = _env?.ContentRootPath ?? Directory.GetCurrentDirectory();
            var baseDir = AppContext.BaseDirectory;
            var candidates = new List<string>
            {
                Path.Combine(cr, "database"),
                Path.Combine(cr, "..", "database"),
                Path.Combine(cr, "..", "..", "database"),
                Path.Combine(baseDir, "database"),
                Path.Combine(baseDir, "..", "..", "..", "..", "database"),
            };
            foreach (var p in candidates.Select(Path.GetFullPath))
            {
                _logger.LogDebug("🔍 Checking database path: {Path}", p);
                if (Directory.Exists(p)) 
                { 
                    _logger.LogInformation("✅ Database directory found: {Path}", p); 
                    return p; 
                }
            }
            _logger.LogWarning("❌ No database directory found in any candidate location");
            return null;
        }

        private static string ComputeHash(string text)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static int ComputeOrderKey(string fileName)
        {
            // 1) Numeric prefix like 001_*.sql
            var match = Regex.Match(fileName, @"^(\d{1,6})[_-].*");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
                return num;

            // 2) Known priorities by conventional filenames
            // Lower number = earlier execution
            var name = fileName.ToLowerInvariant();
            return name switch
            {
                "schema.sql" => 10,
                "transactions_categorization.sql" => 20,
                "household_receipts.sql" => 30,
                "transactions.sql" => 40,
                "transactions_seed.sql" => 90,
                _ => 1000 // default
            };
        }

        private async Task<bool> IsAlreadyAppliedAsync(string fileName, string hash)
        {
            const string sql = @"IF OBJECT_ID('dbo.__SchemaMigrations','U') IS NULL SELECT 0 ELSE SELECT COUNT(1) FROM dbo.__SchemaMigrations WHERE FileName=@f AND Hash=@h";
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@f", fileName);
            cmd.Parameters.AddWithValue("@h", hash);
            var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            return count > 0;
        }

        private async Task InsertMigrationRowAsync(string fileName, string hash)
        {
            const string sql = @"INSERT INTO dbo.__SchemaMigrations (FileName, Hash, AppliedAt) VALUES (@f, @h, SYSUTCDATETIME())";
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@f", fileName);
            cmd.Parameters.AddWithValue("@h", hash);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task EnsureMigrationsTableAsync()
        {
            const string sql = @"
IF OBJECT_ID('dbo.__SchemaMigrations','U') IS NULL
BEGIN
    CREATE TABLE dbo.__SchemaMigrations (
        MigrationId BIGINT IDENTITY(1,1) PRIMARY KEY,
        FileName NVARCHAR(260) NOT NULL,
        Hash NVARCHAR(128) NOT NULL,
        AppliedAt DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX___SchemaMigrations_File ON dbo.__SchemaMigrations(FileName);
    PRINT 'Created __SchemaMigrations table';
END
ELSE
BEGIN
    PRINT '__SchemaMigrations table already exists';
END";
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var cmd = new SqlCommand(sql, connection);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ExecuteSqlBatchesAsync(string scriptContent)
        {
            var batches = Regex.Split(scriptContent, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                               .Select(b => b.Trim())
                               .Where(b => !string.IsNullOrWhiteSpace(b))
                               .ToList();
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            foreach (var batch in batches)
            {
                using var cmd = new SqlCommand(batch, connection);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task ExecuteSqlBatchesInTransactionAsync(string scriptContent)
        {
            var batches = Regex.Split(scriptContent, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                               .Select(b => b.Trim())
                               .Where(b => !string.IsNullOrWhiteSpace(b))
                               .ToList();
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var tx = connection.BeginTransaction();
            try
            {
                foreach (var batch in batches)
                {
                    using var cmd = new SqlCommand(batch, connection, tx);
                    await cmd.ExecuteNonQueryAsync();
                }
                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch {}
                throw;
            }
        }

        private async Task ExecuteSqlBatchesResilientAsync(string scriptContent, string fileName = "unknown")
        {
            var batches = Regex.Split(scriptContent, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                               .Select(b => b.Trim())
                               .Where(b => !string.IsNullOrWhiteSpace(b))
                               .ToList();

            _logger.LogDebug("📦 Processing {BatchCount} SQL batches in {FileName}", batches.Count, fileName);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var pending = new List<string>(batches);
            int pass = 0;
            const int maxPasses = 8;
            while (pending.Count > 0 && pass < maxPasses)
            {
                pass++;
                _logger.LogDebug("🔄 Batch execution pass {Pass}/{MaxPasses} - {PendingCount} batches remaining", pass, maxPasses, pending.Count);
                var applied = new List<int>();
                for (int i = 0; i < pending.Count; i++)
                {
                    var sql = pending[i];
                    var batchPreview = sql.Length > 100 ? sql.Substring(0, 100).Replace("\n", " ").Replace("\r", "") + "..." : sql.Replace("\n", " ").Replace("\r", "");
                    try
                    {
                        _logger.LogDebug("⚡ Executing batch {BatchIndex}: {BatchPreview}", i + 1, batchPreview);
                        using var cmd = new SqlCommand(sql, connection);
                        var batchStart = DateTime.Now;
                        await cmd.ExecuteNonQueryAsync();
                        var batchDuration = DateTime.Now - batchStart;
                        _logger.LogDebug("✅ Batch {BatchIndex} completed in {Duration}ms", i + 1, batchDuration.TotalMilliseconds);
                        applied.Add(i);
                    }
                    catch (SqlException ex)
                    {
                        var msg = ex.Message.ToLowerInvariant();
                        // Dependency not ready yet -> defer to next pass
                        if (msg.Contains("does not exist") || msg.Contains("invalid object name") || msg.Contains("references") || msg.Contains("dependent") || msg.Contains("invalid column name"))
                        {
                            continue;
                        }

                        // Idempotent-safe errors we can treat as already applied
                        bool isIdempotentAlreadyApplied =
                            msg.Contains("already an object named") ||
                            msg.Contains("already exists") ||
                            msg.Contains("cannot drop the index") && msg.Contains("does not exist") ||
                            msg.Contains("cannot drop constraint") && msg.Contains("does not exist") ||
                            msg.Contains("there is no such index") ||
                            msg.Contains("the index") && msg.Contains("already exists") ||
                            msg.Contains("column already exists") ||
                            msg.Contains("object already exists");

                        if (isIdempotentAlreadyApplied)
                        {
                            // Consider as successful for idempotency
                            _logger.LogDebug("✅ Batch {BatchIndex} already applied (idempotent): {BatchPreview}", i + 1, batchPreview);
                            applied.Add(i);
                            continue;
                        }

                        // Log failing batch snippet for diagnostics and dump full batch to file
                        var snippet = sql.Length > 300 ? sql.Substring(0, 300) + "..." : sql;
                        _logger.LogError(ex, "SQL error in migration batch snippet:\n{Snippet}", snippet);
                        try
                        {
                            var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                            Directory.CreateDirectory(logsDir);
                            var path = Path.Combine(logsDir, $"migration-failed-{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.sql");
                            File.WriteAllText(path, sql);
                            Console.WriteLine($"Written failing SQL batch to {path}");
                        }
                        catch { }
                        throw;
                    }
                }

                if (applied.Count == 0)
                {
                    _logger.LogError("❌ No batches applied in pass {Pass} for {FileName}", pass, fileName);
                    try
                    {
                        var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                        Directory.CreateDirectory(logsDir);
                        var path = Path.Combine(logsDir, $"migration-pending-batches-pass{pass}-{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.sql");
                        File.WriteAllText(path, string.Join("\n\n-- ===== Pending Batch Separator =====\n\n", pending));
                        _logger.LogError("📄 Pending batches written to: {Path}", path);
                    }
                    catch { }
                    throw new InvalidOperationException($"No progress applying batches in a file at pass {pass}.");
                }
                else
                {
                    _logger.LogDebug("✅ Pass {Pass} completed - applied {AppliedCount}/{TotalCount} batches", pass, applied.Count, pending.Count);
                }

                // remove applied (from end to start to keep indices valid)
                for (int k = applied.Count - 1; k >= 0; k--)
                {
                    pending.RemoveAt(applied[k]);
                }
            }

            if (pending.Count > 0)
            {
                throw new InvalidOperationException($"File batches incomplete after {maxPasses} passes (remaining batches). Consider revising script to be more idempotent.");
            }
        }
    }
}
