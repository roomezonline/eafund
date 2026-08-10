namespace MiniBank.Web.Models
{
    public class PendingReceiptSummary
    {
        public long HouseholdReceiptId { get; set; }
        public long? TargetHouseholdId { get; set; }
        public byte DocumentType { get; set; }
        public string? TransactionType { get; set; }
        public decimal AmountTotal { get; set; }
        public decimal AllocatedAmount { get; set; }
        public decimal Remaining => AmountTotal - AllocatedAmount;
        public DateTime CreatedAt { get; set; }
        public string? Notes { get; set; }
    }
}
