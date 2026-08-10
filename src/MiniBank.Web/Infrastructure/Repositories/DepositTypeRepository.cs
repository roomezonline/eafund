using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class DepositTypeRepository
    {
        protected readonly IDbConnection Connection;
        public DepositTypeRepository(IDbConnection connection) { Connection = connection; }

        public async Task<IEnumerable<DepositType>> GetAllAsync(string? keyword = null, bool? isActive = null, int limit = 500)
        {
            var sql = @"SELECT * FROM DepositTypes WHERE 1=1";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND (Title LIKE @q OR Category LIKE @q)";
                p.Add("q", $"%{keyword}%");
            }
            if (isActive.HasValue)
            {
                sql += " AND IsActive = @isActive";
                p.Add("isActive", isActive.Value);
            }
            sql += " ORDER BY CreatedAt DESC";
            if (limit > 0) sql += $" OFFSET 0 ROWS FETCH NEXT {limit} ROWS ONLY";
            return await Connection.QueryAsync<DepositType>(sql, p);
        }

        public async Task<DepositType?> GetByIdAsync(long id)
        {
            return await Connection.QueryFirstOrDefaultAsync<DepositType>("SELECT * FROM DepositTypes WHERE DepositTypeId=@id", new { id });
        }

        public async Task<long> CreateAsync(DepositType model)
        {
            var sql = @"INSERT INTO DepositTypes (
                            Title, Category, BreakRate,
                            AccrualBasis, PostingFrequency, Compounding, PostingTarget,
                            DayCountConvention, RoundingRule, MinBalanceForInterest,
                            EligibleForLoan, IsActive, HasInterest, CanGuaranteeLoan, ShowInTransactions, IncludeInTotalBalance, CreatedAt, UpdatedAt)
                         OUTPUT INSERTED.DepositTypeId
                         VALUES (
                            @Title, @Category, @BreakRate,
                            @AccrualBasis, @PostingFrequency, @Compounding, @PostingTarget,
                            @DayCountConvention, @RoundingRule, @MinBalanceForInterest,
                            @EligibleForLoan, @IsActive, @HasInterest, @CanGuaranteeLoan, @ShowInTransactions, @IncludeInTotalBalance, GETUTCDATE(), NULL)";
            return await Connection.QuerySingleAsync<long>(sql, model);
        }

        public async Task UpdateAsync(DepositType model)
        {
            var sql = @"UPDATE DepositTypes
                        SET Title=@Title,
                            Category=@Category,
                            BreakRate=@BreakRate,
                            AccrualBasis=@AccrualBasis,
                            PostingFrequency=@PostingFrequency,
                            Compounding=@Compounding,
                            PostingTarget=@PostingTarget,
                            DayCountConvention=@DayCountConvention,
                            RoundingRule=@RoundingRule,
                            MinBalanceForInterest=@MinBalanceForInterest,
                            EligibleForLoan=@EligibleForLoan,
                            IsActive=@IsActive,
                            HasInterest=@HasInterest,
                            CanGuaranteeLoan=@CanGuaranteeLoan,
                            ShowInTransactions=@ShowInTransactions,
                            IncludeInTotalBalance=@IncludeInTotalBalance,
                            UpdatedAt=GETUTCDATE()
                        WHERE DepositTypeId=@DepositTypeId";
            await Connection.ExecuteAsync(sql, model);
        }

        public async Task DeleteAsync(long id)
        {
            await Connection.ExecuteAsync("DELETE FROM DepositTypes WHERE DepositTypeId=@id", new { id });
        }
    }
}
