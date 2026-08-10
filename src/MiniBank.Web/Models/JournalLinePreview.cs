namespace MiniBank.Web.Models
{
    public class JournalLinePreview
    {
        public int LineNo { get; set; }
        public long AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public long? MemberId { get; set; }
        public long? LoanId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
