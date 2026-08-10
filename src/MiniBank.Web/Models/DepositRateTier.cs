namespace MiniBank.Web.Models
{
    public class DepositRateTier
    {
        public long TierId { get; set; }
        public long PlanId { get; set; }
        public int OrderNo { get; set; }
        public decimal FromAmount { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal RatePercent { get; set; }
    }
}
