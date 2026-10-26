namespace MiniBank.Web.Models
{
    public class BankCard
    {
        public long CardId { get; set; }
        public string BankName { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string? AccountNumber { get; set; }
        public string? Shaba { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
