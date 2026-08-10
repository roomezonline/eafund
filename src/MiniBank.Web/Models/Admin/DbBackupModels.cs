namespace MiniBank.Web.Models.Admin
{
    public enum DbBackupFrequency
    {
        Daily = 1,
        Weekly = 2,
        Monthly = 3
    }

    public sealed class DbBackupSettings
    {
        public int SettingsId { get; set; } = 1;
        public bool IsEnabled { get; set; }
        public string? DatabaseName { get; set; }
        public string? BackupDirectory { get; set; }
        public DbBackupFrequency Frequency { get; set; } = DbBackupFrequency.Daily;
        public int RunAtTimeLocalMinutes { get; set; } = 2 * 60;
        public int? RunOnDayOfWeek { get; set; }
        public int? RunOnDayOfMonth { get; set; }
        public int RetainLastCount { get; set; } = 2;
        public string? NotifyEmailsCsv { get; set; }
        public string? SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;
        public string? SmtpUsername { get; set; }
        public string? SmtpPassword { get; set; }
        public string? SmtpFrom { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class DbBackupRun
    {
        public long RunId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public string Status { get; set; } = "";
        public string? BackupFilePath { get; set; }
        public long? BackupFileSizeBytes { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
