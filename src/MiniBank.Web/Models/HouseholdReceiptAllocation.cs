namespace MiniBank.Web.Models
{
    public class HouseholdReceiptAllocation
    {
        public long AllocationId { get; set; }
        public long HouseholdReceiptId { get; set; }
        public long? MemberId { get; set; }
        public decimal Amount { get; set; }
        public byte PurposeType { get; set; }
        public int? PostingTemplateId { get; set; }
        public decimal? AmountPrincipal { get; set; }
        public decimal? AmountInterest { get; set; }
        public decimal? AmountPenalty { get; set; }
        public byte Status { get; set; } // 0:planned, 1:processed, 2:failed
        public int SortOrder { get; set; }
        public string? Notes { get; set; }
    }
}
