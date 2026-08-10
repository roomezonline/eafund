namespace MiniBank.Web.Models
{
    public class TransactionSummaryRow
    {
        public long GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public long? SubgroupId { get; set; }
        public string? SubgroupName { get; set; }
        public long TypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public int TxCount { get; set; }
        public decimal AmountTotal { get; set; }
    }
}
