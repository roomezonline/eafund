using Dapper;
using MiniBank.Web.Models;
using System.Data;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class MemberRepository
    {
        protected readonly IDbConnection Connection;

        public sealed class MemberDeleteDependencyInfo
        {
            public int AccountsCount { get; set; }
            public int TransactionsCount { get; set; }
            public int JournalLinesCount { get; set; }
            public int HouseholdsOwnedCount { get; set; }

            public bool CanDelete => AccountsCount == 0 && TransactionsCount == 0 && JournalLinesCount == 0 && HouseholdsOwnedCount == 0;

            public string GetDependencyMessage()
            {
                var parts = new List<string>();
                if (HouseholdsOwnedCount > 0) parts.Add($"سرپرست خانوار ({HouseholdsOwnedCount})");
                if (AccountsCount > 0) parts.Add($"حساب/کد تفضیلی مرتبط ({AccountsCount})");
                if (TransactionsCount > 0) parts.Add($"تراکنش مرتبط ({TransactionsCount})");
                if (JournalLinesCount > 0) parts.Add($"سند/خط سند مرتبط ({JournalLinesCount})");
                return parts.Count == 0 ? "" : string.Join("، ", parts);
            }
        }

        public MemberRepository(IDbConnection connection)
        {
            Connection = connection;
        }

        public async Task<IEnumerable<Member>> GetAllAsync(string? searchTerm = null, bool activeOnly = false, long? householdId = null, int limit = 200)
        {
            var sql = @"
                SELECT
                       m.MemberId, m.FirstName, m.FatherName, m.LastName, m.NationalCode, m.MobileNumber,
                       m.HouseholdCode, m.IsActive, m.JoinedAt, m.CreatedAt,
                       h.HouseholdId, h.HouseholdCode, h.Name, h.OwnerMemberId, h.IsActive, h.CreatedAt
                FROM Members m
                LEFT JOIN Households h ON h.HouseholdCode = m.HouseholdCode
                WHERE 1=1";

            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                sql += " AND (m.FirstName LIKE @SearchTerm OR m.FatherName LIKE @SearchTerm OR m.LastName LIKE @SearchTerm OR m.NationalCode LIKE @SearchTerm OR m.MobileNumber LIKE @SearchTerm OR h.Name LIKE @SearchTerm OR h.HouseholdCode LIKE @SearchTerm)";
                parameters.Add("SearchTerm", $"%{searchTerm}%");
            }

            if (activeOnly)
            {
                sql += " AND m.IsActive = 1";
            }

            if (householdId.HasValue)
            {
                // Keep backward-compatible filter by numeric household id using joined Households
                sql += " AND h.HouseholdId = @HouseholdId";
                parameters.Add("HouseholdId", householdId.Value);
            }

            sql += " ORDER BY m.CreatedAt DESC";

            if (limit > 0)
            {
                sql += $" OFFSET 0 ROWS FETCH NEXT {limit} ROWS ONLY";
            }

            var members = await Connection.QueryAsync<Member, Household, Member>(
                sql,
                (member, household) =>
                {
                    if (household != null)
                    {
                        member.Household = household;
                    }
                    return member;
                },
                parameters,
                splitOn: "HouseholdId"
            );

            return members;
        }

        public async Task<Member?> GetByIdAsync(long id)
        {
            var sql = @"
                SELECT
                       m.MemberId, m.FirstName, m.FatherName, m.LastName, m.NationalCode, m.MobileNumber,
                       m.HouseholdCode, m.IsActive, m.JoinedAt, m.CreatedAt,
                       h.HouseholdId, h.HouseholdCode, h.Name, h.OwnerMemberId, h.IsActive, h.CreatedAt
                FROM Members m
                LEFT JOIN Households h ON h.HouseholdCode = m.HouseholdCode
                WHERE m.MemberId = @MemberId";

            var members = await Connection.QueryAsync<Member, Household, Member>(
                sql,
                (member, household) =>
                {
                    if (household != null)
                    {
                        member.Household = household;
                    }
                    return member;
                },
                new { MemberId = id },
                splitOn: "HouseholdId"
            );

            return members.FirstOrDefault();
        }

        public async Task<IReadOnlyList<Member>> GetByIdsAsync(IReadOnlyCollection<long> ids)
        {
            if (ids == null || ids.Count == 0)
                return Array.Empty<Member>();

            var sql = @"
                SELECT
                       m.MemberId, m.FirstName, m.FatherName, m.LastName, m.NationalCode, m.MobileNumber,
                       m.HouseholdCode, m.IsActive, m.JoinedAt, m.CreatedAt,
                       h.HouseholdId, h.HouseholdCode, h.Name, h.OwnerMemberId, h.IsActive, h.CreatedAt
                FROM Members m
                LEFT JOIN Households h ON h.HouseholdCode = m.HouseholdCode
                WHERE m.MemberId IN @Ids";

            var rows = await Connection.QueryAsync<Member, Household, Member>(
                sql,
                (member, household) =>
                {
                    if (household != null)
                        member.Household = household;
                    return member;
                },
                new { Ids = ids.ToArray() },
                splitOn: "HouseholdId");

            return rows.ToList();
        }

        public async Task<Member?> GetByNationalCodeAsync(string nationalCode)
        {
            nationalCode = (nationalCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nationalCode))
                return null;

            var sql = @"
                SELECT TOP 1
                       m.MemberId, m.FirstName, m.FatherName, m.LastName, m.NationalCode, m.MobileNumber,
                       m.HouseholdCode, m.IsActive, m.JoinedAt, m.CreatedAt,
                       h.HouseholdId, h.HouseholdCode, h.Name, h.OwnerMemberId, h.IsActive, h.CreatedAt
                FROM Members m
                LEFT JOIN Households h ON h.HouseholdCode = m.HouseholdCode
                WHERE m.NationalCode = @NationalCode";

            var members = await Connection.QueryAsync<Member, Household, Member>(
                sql,
                (member, household) =>
                {
                    if (household != null)
                        member.Household = household;
                    return member;
                },
                new { NationalCode = nationalCode },
                splitOn: "HouseholdId");

            return members.FirstOrDefault();
        }

        public async Task<long> CreateAsync(Member member)
        {
            // If MemberId is provided (> 0), insert explicitly (used when IDENTITY is removed and IDs are manual)
            if (member.MemberId > 0)
            {
                var sqlWithId = @"
                BEGIN TRY
                    SET IDENTITY_INSERT Members ON;
                END TRY
                BEGIN CATCH
                    -- ignore if not identity
                END CATCH;
                INSERT INTO Members (MemberId, FirstName, FatherName, LastName, NationalCode, MobileNumber, HouseholdCode, IsActive, JoinedAt, CreatedAt)
                VALUES (@MemberId, @FirstName, @FatherName, @LastName, @NationalCode, @MobileNumber,
                        @HouseholdCode,
                        @IsActive, @JoinedAt, @CreatedAt);
                BEGIN TRY
                    SET IDENTITY_INSERT Members OFF;
                END TRY
                BEGIN CATCH
                    -- ignore if not identity
                END CATCH;
                SELECT CAST(@MemberId AS BIGINT);";

                var idWithExplicit = await Connection.QuerySingleAsync<long>(sqlWithId, new
                {
                    member.MemberId,
                    member.FirstName,
                    member.LastName,
                    member.FatherName,
                    member.NationalCode,
                    member.MobileNumber,
                    member.HouseholdCode,
                    member.IsActive,
                    member.JoinedAt,
                    CreatedAt = DateTime.UtcNow
                });

                return idWithExplicit;
            }
            else
            {
                // Default path: let DB assign (works if column is IDENTITY)
                var sql = @"
                INSERT INTO Members (FirstName, FatherName, LastName, NationalCode, MobileNumber, HouseholdCode, IsActive, JoinedAt, CreatedAt)
                OUTPUT INSERTED.MemberId
                VALUES (@FirstName, @FatherName, @LastName, @NationalCode, @MobileNumber,
                        @HouseholdCode,
                        @IsActive, @JoinedAt, @CreatedAt)";

                var id = await Connection.QuerySingleAsync<long>(sql, new
                {
                    member.FirstName,
                    member.FatherName,
                    member.LastName,
                    member.NationalCode,
                    member.MobileNumber,
                    member.HouseholdCode,
                    member.IsActive,
                    member.JoinedAt,
                    CreatedAt = DateTime.UtcNow
                });

                return id;
            }
        }

        public async Task UpdateAsync(Member member)
        {
            var sql = @"
                UPDATE Members 
                SET FirstName = @FirstName,
                    FatherName = @FatherName,
                    LastName = @LastName,
                    NationalCode = @NationalCode,
                    MobileNumber = @MobileNumber,
                    HouseholdCode = @HouseholdCode,
                    IsActive = @IsActive,
                    JoinedAt = @JoinedAt
                WHERE MemberId = @MemberId";

            await Connection.ExecuteAsync(sql, member);
        }

        public async Task DeleteAsync(long id)
        {
            var sql = "DELETE FROM Members WHERE MemberId = @MemberId";
            await Connection.ExecuteAsync(sql, new { MemberId = id });
        }

        public async Task<MemberDeleteDependencyInfo> GetDeleteDependencyInfoAsync(long memberId)
        {
            var info = new MemberDeleteDependencyInfo();

            try
            {
                info.AccountsCount = await Connection.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM dbo.Accounts WHERE MemberId = @memberId", new { memberId });
            }
            catch
            {
                // ignore
            }

            try
            {
                info.TransactionsCount = await Connection.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM dbo.Transactions WHERE MemberId = @memberId", new { memberId });
            }
            catch
            {
                // ignore
            }

            try
            {
                info.JournalLinesCount = await Connection.QuerySingleAsync<int>(
                    @"SELECT COUNT(1) FROM dbo.JournalLines jl
                    INNER JOIN dbo.vw_ApprovedJournals j ON jl.JournalId = j.JournalId
                    WHERE jl.MemberId = @memberId", new { memberId });
            }
            catch
            {
                // ignore
            }

            try
            {
                info.HouseholdsOwnedCount = await Connection.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM dbo.Households WHERE OwnerMemberId = @memberId", new { memberId });
            }
            catch
            {
                // ignore
            }

            return info;
        }

        public async Task<bool> ExistsByNationalCodeAsync(string nationalCode, long? excludeMemberId = null)
        {
            var sql = "SELECT COUNT(1) FROM Members WHERE NationalCode = @NationalCode";
            var parameters = new DynamicParameters();
            parameters.Add("NationalCode", nationalCode);

            if (excludeMemberId.HasValue)
            {
                sql += " AND MemberId != @ExcludeMemberId";
                parameters.Add("ExcludeMemberId", excludeMemberId.Value);
            }

            var count = await Connection.QuerySingleAsync<int>(sql, parameters);
            return count > 0;
        }

        public async Task<long> GetNextMemberIdAsync()
        {
            // Reserve next ID with appropriate locking to avoid race conditions
            var sql = @"
                DECLARE @MaxId BIGINT;
                SELECT @MaxId = MAX(MemberId) FROM Members WITH (UPDLOCK, HOLDLOCK);
                SELECT CASE WHEN @MaxId IS NULL THEN 11001 ELSE @MaxId + 1 END;";
            var nextId = await Connection.QuerySingleAsync<long>(sql);
            return nextId;
        }
    }
}
