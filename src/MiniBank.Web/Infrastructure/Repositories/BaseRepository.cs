using Microsoft.Data.SqlClient;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using MiniBank.Web.Core.Interfaces;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public abstract class BaseRepository<T> : IRepository<T> where T : class, new()
    {
        protected readonly string _connectionString;
        protected readonly ILogger<BaseRepository<T>> _logger;
        protected readonly string _tableName;

        protected BaseRepository(IConfiguration configuration, ILogger<BaseRepository<T>> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string not found");
            _logger = logger;
            _tableName = GetTableName();
        }

        protected virtual string GetTableName()
        {
            return typeof(T).Name + "s"; // Default: ClassName + "s"
        }

        public virtual async Task<T?> GetByIdAsync(long id)
        {
            var sql = $"SELECT * FROM [{_tableName}] WHERE {GetIdColumnName()} = @Id";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);
            
            using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                return MapFromReader(reader);
            }
            
            return null;
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            var sql = $"SELECT * FROM [{_tableName}] ORDER BY {GetIdColumnName()}";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            var results = new List<T>();
            while (await reader.ReadAsync())
            {
                results.Add(MapFromReader(reader));
            }
            
            return results;
        }

        public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            // Simple implementation - for complex queries, consider using a proper ORM
            var allItems = await GetAllAsync();
            return allItems.Where(predicate.Compile());
        }

        public virtual async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
        {
            var items = await FindAsync(predicate);
            return items.FirstOrDefault();
        }

        public virtual async Task<long> AddAsync(T entity)
        {
            var insertSql = GenerateInsertSql();
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(insertSql, connection);
            AddInsertParameters(command, entity);
            
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        public virtual async Task UpdateAsync(T entity)
        {
            var updateSql = GenerateUpdateSql();
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(updateSql, connection);
            AddUpdateParameters(command, entity);
            
            await command.ExecuteNonQueryAsync();
        }

        public virtual async Task DeleteAsync(long id)
        {
            var sql = $"DELETE FROM [{_tableName}] WHERE {GetIdColumnName()} = @Id";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);
            
            await command.ExecuteNonQueryAsync();
        }

        public virtual async Task<bool> ExistsAsync(long id)
        {
            var sql = $"SELECT COUNT(1) FROM [{_tableName}] WHERE {GetIdColumnName()} = @Id";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);
            
            var count = (int)(await command.ExecuteScalarAsync() ?? 0);
            return count > 0;
        }

        public virtual async Task<int> CountAsync()
        {
            var sql = $"SELECT COUNT(*) FROM [{_tableName}]";
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            return (int)(await command.ExecuteScalarAsync() ?? 0);
        }

        public virtual async Task<int> CountAsync(Expression<Func<T, bool>> predicate)
        {
            // Simple implementation
            var items = await FindAsync(predicate);
            return items.Count();
        }

        protected abstract T MapFromReader(SqlDataReader reader);
        protected abstract string GetIdColumnName();
        protected abstract string GenerateInsertSql();
        protected abstract string GenerateUpdateSql();
        protected abstract void AddInsertParameters(SqlCommand command, T entity);
        protected abstract void AddUpdateParameters(SqlCommand command, T entity);

        protected static string GetSafeString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        protected static long? GetSafeNullableLong(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
        }

        protected static bool GetSafeBool(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
        }

        protected static DateTime GetSafeDateTime(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? DateTime.UtcNow : reader.GetDateTime(ordinal);
        }
    }
}
