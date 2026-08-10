namespace MiniBank.Web.Models
{
    public class LoanRatePlan
    {
        public long PlanId { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public decimal AnnualRatePercent { get; set; }
        public decimal PenaltyAnnualRatePercent { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
