namespace MiniBank.Web.Models
{
    public class Transaction
    {
        public long TransactionId { get; set; }
        public long TypeId { get; set; }
        public long? HouseholdId { get; set; }
        public long? MemberId { get; set; }
        public long? DepositTypeId { get; set; }
        public long? MemberDepositAccountId { get; set; }
        public decimal AmountTotal { get; set; }
        public decimal? AmountPrincipal { get; set; }
        public decimal? AmountInterest { get; set; }
        public decimal? AmountPenalty { get; set; }
        public decimal? AmountFee { get; set; }
        public decimal? AmountTax { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public byte Status { get; set; }
        public long? ParentReceiptId { get; set; }
        public long? AllocationId { get; set; }
        public string? Notes { get; set; }
        public string? ExternalRef { get; set; }
        public string? IdempotencyKey { get; set; }
        
        // Per-transaction posting template override (takes precedence over TransactionType.PostingTemplateId)
        public int? PostingTemplateId { get; set; }
        
        // حساب تفصیلی انتخابی برای جایگزینی پویا در الگوهای حسابداری
        public long? SubsidiaryAccountId { get; set; }
    }
}
