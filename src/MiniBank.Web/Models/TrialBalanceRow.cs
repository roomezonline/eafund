namespace MiniBank.Web.Models
{
    public class TrialBalanceRow
    {
        public long AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public byte AccountType { get; set; }
        public byte? Level { get; set; } // Added for hierarchy support
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Net { get; set; }
    }
}
