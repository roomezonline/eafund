using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using MiniBank.Web.Models;

namespace MiniBank.Web.Services
{
    public class CustomerService
    {
        private readonly string _connectionString;
        public CustomerService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                                 ?? throw new System.Exception("Connection string 'DefaultConnection' not found.");
        }

        public async Task<int> CreateCustomerAsync(string? nationalId, string firstName, string lastName, string? phoneNumber)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO core.Customers (NationalID, FirstName, LastName, PhoneNumber, RegistrationDateUTC, IsActive)
VALUES (@NationalID, @FirstName, @LastName, @PhoneNumber, SYSUTCDATETIME(), 1);
SELECT CAST(SCOPE_IDENTITY() AS INT);
";
            cmd.Parameters.Add(new SqlParameter("@NationalID", SqlDbType.VarChar, 10) { Value = (object?)nationalId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@FirstName", SqlDbType.NVarChar, 100) { Value = firstName });
            cmd.Parameters.Add(new SqlParameter("@LastName", SqlDbType.NVarChar, 100) { Value = lastName });
            cmd.Parameters.Add(new SqlParameter("@PhoneNumber", SqlDbType.VarChar, 20) { Value = (object?)phoneNumber ?? DBNull.Value });

            var result = await cmd.ExecuteScalarAsync();
            return (int)(result ?? 0);
        }

        public async Task<IReadOnlyList<CustomerDto>> GetCustomersAsync(string? search = null, int take = 100, int skip = 0)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var where = string.Empty;
            if (!string.IsNullOrWhiteSpace(search))
            {
                where = "WHERE FirstName LIKE @q OR LastName LIKE @q OR NationalID LIKE @q OR PhoneNumber LIKE @q";
            }

            var sql = $@"
SELECT CustomerID, NationalID, FirstName, LastName, PhoneNumber, IsActive,
       CONVERT(varchar(19), RegistrationDateUTC, 126) as RegistrationDateUTC
FROM core.Customers
{where}
ORDER BY CustomerID DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@skip", SqlDbType.Int) { Value = skip });
            cmd.Parameters.Add(new SqlParameter("@take", SqlDbType.Int) { Value = take });
            if (!string.IsNullOrWhiteSpace(search))
            {
                cmd.Parameters.Add(new SqlParameter("@q", SqlDbType.NVarChar, 128) { Value = "%" + search + "%" });
            }

            var list = new List<CustomerDto>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new CustomerDto(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetBoolean(5),
                    reader.GetString(6)
                ));
            }
            return list;
        }

        public async Task ToggleActiveAsync(int customerId, bool isActive)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE core.Customers SET IsActive=@active, UpdatedAtUTC=SYSUTCDATETIME() WHERE CustomerID=@id";
            cmd.Parameters.Add(new SqlParameter("@active", SqlDbType.Bit) { Value = isActive });
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = customerId });
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
