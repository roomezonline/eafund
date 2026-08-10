namespace MiniBank.Web.Models
{
    public class TransactionAuditEntry
    {
        public DateTime CreatedAt { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string? Message { get; set; }
    }
}
