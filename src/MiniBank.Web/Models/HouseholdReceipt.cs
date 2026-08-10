namespace MiniBank.Web.Models
{
    public class HouseholdReceipt
    {
        public long HouseholdReceiptId { get; set; }
        public long? TargetHouseholdId { get; set; }
        public byte DocumentType { get; set; } // 1: اعضایی, 2: غیراعضایی
        public string? TransactionType { get; set; }
        public string? CounterpartyName { get; set; }
        public decimal AmountTotal { get; set; }
        public byte PayChannel { get; set; } // 1: بانک, 2: بورس
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public byte Status { get; set; } // 0:pending
        public string? Notes { get; set; }
        public string? ExternalRef { get; set; }
    }
}
