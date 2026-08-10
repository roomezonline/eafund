namespace MiniBank.Web.Models
{
    public class FiscalYearBalance
    {
        public long BalanceId { get; set; }
        public int FiscalYearId { get; set; }
        public long AccountId { get; set; }
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
    }
}
