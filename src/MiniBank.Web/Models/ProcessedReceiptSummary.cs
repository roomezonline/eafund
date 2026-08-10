namespace MiniBank.Web.Models
{
    public class ProcessedReceiptSummary
    {
        public long HouseholdReceiptId { get; set; }
        public long TargetHouseholdId { get; set; }
        public decimal AmountTotal { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Notes { get; set; }
        public List<ProcessedReceiptAllocation>? Allocations { get; set; }
    }

    public class ProcessedReceiptAllocation
    {
        public long MemberId { get; set; }
        public byte PurposeType { get; set; }
        public decimal Amount { get; set; }
        public long? DepositTypeId { get; set; }
    }
}
