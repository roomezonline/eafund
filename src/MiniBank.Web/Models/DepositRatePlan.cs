namespace MiniBank.Web.Models
{
    public enum DepositRateMode : byte
    {
        Single = 1,
        Tiered = 2
    }

    public class DepositRatePlan
    {
        public long PlanId { get; set; }
        public DepositRateMode Mode { get; set; } = DepositRateMode.Single;
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public decimal? SingleRatePercent { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
