namespace MiniBank.Web.Models
{
    public class ReceiptDeletionInfo
    {
        public int TransactionsCount { get; set; }
        public int JournalsCount { get; set; }
        public int JournalLinesCount { get; set; }
        public int AllocationsCount { get; set; }
    }
}
