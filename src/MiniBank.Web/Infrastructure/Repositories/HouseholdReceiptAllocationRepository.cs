using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class HouseholdReceiptAllocationRepository
    {
        private readonly IDbConnection _connection;
        public HouseholdReceiptAllocationRepository(IDbConnection connection) { _connection = connection; }

        public async Task<long> CreateAsync(HouseholdReceiptAllocation model)
        {
            if (_connection.State != ConnectionState.Open)
                _connection.Open();
            const string sql = @"
INSERT INTO dbo.HouseholdReceiptAllocations (
    HouseholdReceiptId, MemberId, Amount,
    PurposeType, PostingTemplateId, AmountPrincipal, AmountInterest, AmountPenalty,
    Status, SortOrder, Notes
) OUTPUT INSERTED.AllocationId
VALUES (
    @HouseholdReceiptId, @MemberId, @Amount,
    @PurposeType, @PostingTemplateId, @AmountPrincipal, @AmountInterest, @AmountPenalty,
    @Status, @SortOrder, @Notes
);";
            return await _connection.QuerySingleAsync<long>(sql, model);
        }

        public async Task<int> BulkCreateAsync(IEnumerable<HouseholdReceiptAllocation> items)
        {
            int total = 0;
            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            using var tx = _connection.BeginTransaction();
            try
            {
                foreach (var item in items)
                {
                    const string sql = @"
INSERT INTO dbo.HouseholdReceiptAllocations (
    HouseholdReceiptId, MemberId, Amount,
    PurposeType, PostingTemplateId, AmountPrincipal, AmountInterest, AmountPenalty,
    Status, SortOrder, Notes
) VALUES (
    @HouseholdReceiptId, @MemberId, @Amount,
    @PurposeType, @PostingTemplateId, @AmountPrincipal, @AmountInterest, @AmountPenalty,
    @Status, @SortOrder, @Notes
);";
                    await _connection.ExecuteAsync(sql, item, tx);
                    total++;
                }
                tx.Commit();
                return total;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<IEnumerable<HouseholdReceiptAllocation>> GetByReceiptAsync(long receiptId)
        {
            const string sql = "SELECT * FROM dbo.HouseholdReceiptAllocations WHERE HouseholdReceiptId = @receiptId ORDER BY SortOrder";
            return await _connection.QueryAsync<HouseholdReceiptAllocation>(sql, new { receiptId });
        }

        public async Task<bool> DeleteByReceiptAsync(long receiptId)
        {
            const string sql = "DELETE FROM dbo.HouseholdReceiptAllocations WHERE HouseholdReceiptId = @receiptId";
            var affected = await _connection.ExecuteAsync(sql, new { receiptId });
            return affected > 0;
        }
    }
}
