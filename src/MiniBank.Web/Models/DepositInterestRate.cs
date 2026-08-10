namespace MiniBank.Web.Models
{
    public class DepositInterestRate
    {
        public long RateId { get; set; }
        public long DepositTypeId { get; set; }
        public decimal InterestRate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
