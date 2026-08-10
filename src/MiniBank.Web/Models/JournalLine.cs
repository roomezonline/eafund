namespace MiniBank.Web.Models
{
    public class JournalLine
    {
        public long JournalLineId { get; set; }
        public long JournalId { get; set; }
        public int LineNo { get; set; }
        public long AccountId { get; set; }
        public long? MemberId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string? Description { get; set; }
        public long? LoanId { get; set; }
        
        // Navigation
        public Journal Journal { get; set; } = null!;
        public Account Account { get; set; } = null!;
        public Member? Member { get; set; }
    }
}
