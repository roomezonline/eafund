namespace MiniBank.Web.Models
{
    public class Journal
    {
        public long JournalId { get; set; }
        public long? TransactionId { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public byte Status { get; set; } = 2;
        public bool IsDeleted { get; set; }
        public int? SerialNumber { get; set; }
        public int? FiscalYearId { get; set; }
        public int? DocumentNumber { get; set; }

        // Navigation
        public List<JournalLine> Lines { get; set; } = new();
    }
}
