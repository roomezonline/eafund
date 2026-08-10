using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;

namespace MiniBank.ConstraintFix
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var connectionString = "Server=185.211.56.76,1463;Database=foodir_EmamAli_minibank;User Id=foodir;Password=M2223125!;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true;Connection Timeout=30;Command Timeout=60;";
            
            Console.WriteLine("🔧 Starting direct constraint fix...");
            
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                Console.WriteLine("✅ Connected to database successfully");
                
                // Check if constraint exists
                var checkSql = "SELECT COUNT(1) FROM sys.check_constraints WHERE name = N'CK_Accounts_Hierarchy'";
                using var checkCmd = new SqlCommand(checkSql, connection);
                var constraintExists = (int)await checkCmd.ExecuteScalarAsync() > 0;
                
                if (constraintExists)
                {
                    Console.WriteLine("🔍 Found existing CK_Accounts_Hierarchy constraint - dropping it...");
                    
                    // Drop existing constraint
                    var dropSql = "ALTER TABLE [dbo].[Accounts] DROP CONSTRAINT [CK_Accounts_Hierarchy];";
                    using var dropCmd = new SqlCommand(dropSql, connection);
                    await dropCmd.ExecuteNonQueryAsync();
                    Console.WriteLine("✅ Old constraint dropped successfully");
                }
                else
                {
                    Console.WriteLine("ℹ️ CK_Accounts_Hierarchy constraint does not exist");
                }
                
                // Add updated constraint
                Console.WriteLine("🔧 Adding new CK_Accounts_Hierarchy constraint...");
                var addSql = @"
                    ALTER TABLE [dbo].[Accounts] 
                    ADD CONSTRAINT [CK_Accounts_Hierarchy] 
                    CHECK (
                        (Level = 1 AND ParentAccountId IS NULL) OR
                        (Level = 2 AND ParentAccountId IS NOT NULL) OR  
                        (Level = 3 AND ParentAccountId IS NOT NULL) OR
                        (Level = 4) OR -- Level 4 can have optional parent
                        (Level IS NULL)
                    );";
                
                using var addCmd = new SqlCommand(addSql, connection);
                await addCmd.ExecuteNonQueryAsync();
                Console.WriteLine("✅ New constraint added successfully!");
                
                // Verify constraint was created
                var verifyExists = (int)await checkCmd.ExecuteScalarAsync() > 0;
                if (verifyExists)
                {
                    Console.WriteLine("🎉 SUCCESS: CK_Accounts_Hierarchy constraint is now active");
                    Console.WriteLine("📝 Level 4 accounts can now have optional parents");
                    
                    // Show constraint definition
                    var defSql = @"
                        SELECT cc.definition 
                        FROM sys.check_constraints cc
                        JOIN sys.tables t ON cc.parent_object_id = t.object_id
                        WHERE cc.name = N'CK_Accounts_Hierarchy' AND t.name = N'Accounts'";
                    
                    using var defCmd = new SqlCommand(defSql, connection);
                    var definition = await defCmd.ExecuteScalarAsync() as string;
                    Console.WriteLine($"📋 Constraint definition: {definition}");
                }
                else
                {
                    Console.WriteLine("❌ ERROR: Failed to create CK_Accounts_Hierarchy constraint");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine($"📝 Details: {ex}");
            }
            
            Console.WriteLine("🏁 Constraint fix completed!");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
