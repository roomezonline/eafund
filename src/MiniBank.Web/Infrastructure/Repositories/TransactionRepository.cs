using Dapper;
using System.Data;
using MiniBank.Web.Models;

namespace MiniBank.Web.Infrastructure.Repositories
{
    public class TransactionRepository
    {
        private readonly IDbConnection _connection;
        public TransactionRepository(IDbConnection connection) { _connection = connection; }

        public async Task<long> CreateAsync(Transaction model)
        {
            const string sql = @"
INSERT INTO dbo.Transactions (
    TypeId, HouseholdId, MemberId, DepositTypeId, AmountTotal,
    AmountPrincipal, AmountInterest, AmountPenalty, AmountFee, AmountTax,
    OccurredAt, CreatedAt, CreatedBy, Status, ParentReceiptId, AllocationId, Notes, ExternalRef, IdempotencyKey, PostingTemplateId, SubsidiaryAccountId
) OUTPUT INSERTED.TransactionId
VALUES (
    @TypeId, @HouseholdId, @MemberId, @DepositTypeId, @AmountTotal,
    @AmountPrincipal, @AmountInterest, @AmountPenalty, @AmountFee, @AmountTax,
    @OccurredAt, GETUTCDATE(), @CreatedBy, @Status, @ParentReceiptId, @AllocationId, @Notes, @ExternalRef, @IdempotencyKey, @PostingTemplateId, @SubsidiaryAccountId
);";
            return await _connection.QuerySingleAsync<long>(sql, model);
        }

        public async Task<Transaction?> GetByIdAsync(long id)
        {
            const string sql = "SELECT * FROM dbo.Transactions WHERE TransactionId=@id";
            return await _connection.QueryFirstOrDefaultAsync<Transaction>(sql, new { id });
        }
    }
}
