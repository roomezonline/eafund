using Microsoft.Data.SqlClient;
using MiniBank.Web.Core.Interfaces;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class HouseholdRepository : BaseRepository<Household>, IHouseholdRepository
    {
        public HouseholdRepository(IConfiguration configuration, ILogger<HouseholdRepository> logger)
            : base(configuration, logger)
        {
        }

        protected override string GetTableName() => "Households";
        protected override string GetIdColumnName() => "HouseholdId";

        protected override Household MapFromReader(SqlDataReader reader)
        {
            return new Household
            {
                HouseholdId = reader.GetInt64(reader.GetOrdinal("HouseholdId")),
                HouseholdCode = GetSafeString(reader, "HouseholdCode"),
                Name = GetSafeString(reader, "Name"),
                OwnerMemberId = GetSafeNullableLong(reader, "OwnerMemberId"),
                IsActive = GetSafeBool(reader, "IsActive"),
                CreatedAt = GetSafeDateTime(reader, "CreatedAt")
            };
        }

        protected override string GenerateInsertSql()
        {
            return @"
                INSERT INTO [Households] (HouseholdCode, Name, OwnerMemberId, IsActive, CreatedAt)
                OUTPUT INSERTED.HouseholdId
                VALUES (@HouseholdCode, @Name, @OwnerMemberId, @IsActive, @CreatedAt)";
        }

        protected override string GenerateUpdateSql()
        {
            return @"
                UPDATE [Households] 
                SET HouseholdCode = @HouseholdCode,
                    Name = @Name,
                    OwnerMemberId = @OwnerMemberId,
                    IsActive = @IsActive
                WHERE HouseholdId = @HouseholdId";
        }

        protected override void AddInsertParameters(SqlCommand command, Household entity)
        {
            command.Parameters.AddWithValue("@HouseholdCode", entity.HouseholdCode ?? string.Empty);
            command.Parameters.AddWithValue("@Name", entity.Name ?? string.Empty);
            command.Parameters.AddWithValue("@OwnerMemberId", entity.OwnerMemberId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@IsActive", entity.IsActive);
            command.Parameters.AddWithValue("@CreatedAt", entity.CreatedAt);
        }

        protected override void AddUpdateParameters(SqlCommand command, Household entity)
        {
            command.Parameters.AddWithValue("@HouseholdId", entity.HouseholdId);
            command.Parameters.AddWithValue("@HouseholdCode", entity.HouseholdCode ?? string.Empty);
            command.Parameters.AddWithValue("@Name", entity.Name ?? string.Empty);
            command.Parameters.AddWithValue("@OwnerMemberId", entity.OwnerMemberId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@IsActive", entity.IsActive);
        }

        public async Task<IEnumerable<Household>> GetActiveHouseholdsAsync()
        {
            const string sql = "SELECT * FROM [Households] WHERE IsActive = 1 ORDER BY Name";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            var results = new List<Household>();
            while (await reader.ReadAsync())
            {
                results.Add(MapFromReader(reader));
            }
            
            return results;
        }

        public async Task<IEnumerable<Household>> SearchHouseholdsAsync(string searchTerm)
        {
            const string sql = @"
                SELECT * FROM [Households] 
                WHERE (HouseholdCode LIKE @SearchTerm OR Name LIKE @SearchTerm)
                ORDER BY Name";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");
            
            using var reader = await command.ExecuteReaderAsync();
            
            var results = new List<Household>();
            while (await reader.ReadAsync())
            {
                results.Add(MapFromReader(reader));
            }
            
            return results;
        }

        public async Task<Household?> GetByCodeAsync(string householdCode)
        {
            const string sql = "SELECT * FROM [Households] WHERE HouseholdCode = @HouseholdCode";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@HouseholdCode", householdCode);
            
            using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                return MapFromReader(reader);
            }
            
            return null;
        }

        public async Task<bool> IsCodeUniqueAsync(string householdCode, long? excludeId = null)
        {
            var sql = "SELECT COUNT(1) FROM [Households] WHERE HouseholdCode = @HouseholdCode";
            
            if (excludeId.HasValue)
            {
                sql += " AND HouseholdId != @ExcludeId";
            }
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@HouseholdCode", householdCode);
            
            if (excludeId.HasValue)
            {
                command.Parameters.AddWithValue("@ExcludeId", excludeId.Value);
            }
            
            var count = (int)(await command.ExecuteScalarAsync() ?? 0);
            // Unique when there are no other rows with the same code (respecting excludeId)
            return count == 0;
        }

        public async Task<IEnumerable<Household>> GetRecentHouseholdsAsync(int count = 10)
        {
            const string sql = "SELECT TOP (@Count) * FROM [Households] ORDER BY CreatedAt DESC";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Count", count);
            
            using var reader = await command.ExecuteReaderAsync();
            
            var results = new List<Household>();
            while (await reader.ReadAsync())
            {
                results.Add(MapFromReader(reader));
            }
            
            return results;
        }
    }
}
