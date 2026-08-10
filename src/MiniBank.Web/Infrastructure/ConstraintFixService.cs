using Microsoft.Data.SqlClient;
using System.Data;

namespace MiniBank.Web.Infrastructure
{
    public class ConstraintFixService
    {
        private readonly IDbConnection _connection;
        private readonly ILogger<ConstraintFixService> _logger;

        public ConstraintFixService(IDbConnection connection, ILogger<ConstraintFixService> logger)
        {
            _connection = connection;
            _logger = logger;
        }

        public async Task FixSubsidiaryConstraintAsync()
        {
            try
            {
                _logger.LogInformation("🔧 Checking and fixing CK_Accounts_Hierarchy constraint...");

                // Ensure connection is open
                if (_connection.State != ConnectionState.Open)
                {
                    if (_connection is SqlConnection sqlConn)
                        await sqlConn.OpenAsync();
                    else
                        _connection.Open();
                }

                // Check if constraint exists and needs fixing
                var constraintExists = await CheckConstraintExistsAsync();
                if (!constraintExists)
                {
                    _logger.LogInformation("✅ CK_Accounts_Hierarchy constraint does not exist - no fix needed");
                    return;
                }

                // Check if constraint already allows optional parents for Level 4
                var needsFix = await CheckConstraintNeedsFixAsync();
                if (!needsFix)
                {
                    _logger.LogInformation("✅ CK_Accounts_Hierarchy constraint already allows optional parents for Level 4");
                    return;
                }

                _logger.LogInformation("🔄 Updating CK_Accounts_Hierarchy constraint to allow optional parents for Level 4...");

                // Drop existing constraint
                await ExecuteSqlAsync(@"
                    ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_Hierarchy];
                    PRINT 'Dropped old CK_Accounts_Hierarchy constraint';
                ");

                // Add updated constraint
                await ExecuteSqlAsync(@"
                    ALTER TABLE [dbo].[Accounts] 
                    ADD CONSTRAINT [CK_Accounts_Hierarchy] 
                    CHECK (
                        (Level = 1 AND ParentAccountId IS NULL) OR
                        (Level = 2 AND ParentAccountId IS NOT NULL) OR  
                        (Level = 3 AND ParentAccountId IS NOT NULL) OR
                        (Level = 4) OR -- Level 4 can have optional parent
                        (Level IS NULL)
                    );
                    PRINT 'Added updated CK_Accounts_Hierarchy constraint - Level 4 can now have optional parent';
                ");

                _logger.LogInformation("✅ Successfully updated CK_Accounts_Hierarchy constraint");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to fix CK_Accounts_Hierarchy constraint: {Error}", ex.Message);
                throw;
            }
        }

        private async Task<bool> CheckConstraintExistsAsync()
        {
            var sql = "SELECT COUNT(1) FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy'";
            var result = await ExecuteScalarAsync<int>(sql);
            return result > 0;
        }

        private async Task<bool> CheckConstraintNeedsFixAsync()
        {
            // Get constraint definition
            var sql = @"
                SELECT definition 
                FROM sys.check_constraints cc
                JOIN sys.objects o ON cc.parent_object_id = o.object_id
                WHERE cc.name = N'CK_Accounts_Hierarchy' AND o.name = N'Accounts'";
            
            var definition = await ExecuteScalarAsync<string>(sql);
            if (string.IsNullOrEmpty(definition))
                return false;

            // Check if definition already allows Level 4 with optional parent
            var needsFix = !definition.Contains("(Level = 4)") && 
                          definition.Contains("(Level = 4 AND ParentAccountId IS NOT NULL)");
            
            return needsFix;
        }

        private async Task ExecuteSqlAsync(string sql)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 60;
            await ((SqlCommand)command).ExecuteNonQueryAsync();
        }

        private async Task<T> ExecuteScalarAsync<T>(string sql)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;
            var result = await ((SqlCommand)command).ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? default(T) : (T)result;
        }
    }
}
