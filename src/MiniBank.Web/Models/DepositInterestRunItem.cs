namespace MiniBank.Web.Models
{
    public sealed class DepositInterestRunItem
    {
        public long RunItemId { get; set; }
        public long RunId { get; set; }
        public long MemberId { get; set; }
        public string? MemberCode { get; set; }
        public string MemberName { get; set; } = string.Empty;

        public decimal NetPrincipal { get; set; }
        public decimal NetInterest { get; set; }
        public decimal TotalNet { get; set; }
        public decimal MonthProfit { get; set; }

        public bool IsNeutral { get; set; }
        public long? JournalId { get; set; }
        public int? DocumentNumber { get; set; }
    }
}
