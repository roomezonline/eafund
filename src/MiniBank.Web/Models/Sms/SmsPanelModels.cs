namespace MiniBank.Web.Models.Sms
{
    public class SmsSettings
    {
        public int SettingsId { get; set; }
        public bool IsEnabled { get; set; }
        public string? ProviderName { get; set; }
        public string? VerifyTemplateId { get; set; }
        public long CreditRial { get; set; }
        public int PartCostRial { get; set; }
        public long LowCreditThresholdRial { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SmsFeature
    {
        public string FeatureKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SmsCreditTopup
    {
        public long TopupId { get; set; }
        public long AmountRial { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SmsMessageLog
    {
        public long LogId { get; set; }
        public string? FeatureKey { get; set; }
        public long? HouseholdId { get; set; }
        public long? HeadMemberId { get; set; }
        public string? MemberIdsJson { get; set; }
        public string? RecipientMobile { get; set; }
        public int? TemplateId { get; set; }
        public string? ParamsJson { get; set; }
        public string? MessageText { get; set; }
        public long? ProviderMessageId { get; set; }
        public int? ApiStatus { get; set; }
        public string? ApiMessage { get; set; }
        public byte? DeliveryState { get; set; }
        public DateTime? DeliveryDateTime { get; set; }
        public bool? IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RawResponse { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class SmsMessageLogReportRow : SmsMessageLog
    {
        public string? HeadFullName { get; set; }
        public string? HeadMobileNumber { get; set; }
        public string? HeadHouseholdCode { get; set; }
    }
}
