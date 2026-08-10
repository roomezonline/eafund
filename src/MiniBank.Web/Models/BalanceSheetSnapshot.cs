namespace MiniBank.Web.Models
{
    public class BalanceSheetSnapshot
    {
        public decimal Assets { get; set; }
        public decimal Liabilities { get; set; }
        public decimal Equity { get; set; }
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
    }
}
