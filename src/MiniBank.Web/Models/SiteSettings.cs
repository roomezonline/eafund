namespace MiniBank.Web.Models
{
    public sealed class SiteSettings
    {
        public int Id { get; set; }
        public bool ShowSpecialOffers { get; set; } = true;
        public bool ShowLatestNews { get; set; } = true;
        public bool ShowLatestProducts { get; set; } = true;
        public bool ShowLatestAcademy { get; set; } = true;
        public bool ShowLatestCourses { get; set; } = true;
        public bool ShowLatestArticles { get; set; } = true;
        public bool ShowTeamMembers { get; set; } = true;
        public bool ShowHomeCategories { get; set; } = true;
        public bool ShowFeatureCards { get; set; } = true;
        public bool ShowStories { get; set; } = true;
        public string? SeoTitle { get; set; }
        public string? SeoDescription { get; set; }
        public string? SeoKeywords { get; set; }
        public string? GoogleAnalyticsTag { get; set; }
        public string? DefaultOgImage { get; set; }
        public bool IsMaintenanceMode { get; set; } = false;
        public bool EnableSsl { get; set; } = true;
        public int HstsMaxAgeDays { get; set; } = 7;
        public bool AllowGuestCheckout { get; set; } = true;
        public bool EnableOnlineChat { get; set; } = true;
        public bool AllowGuestChat { get; set; } = true;
        public int MaxImageUploadSizeKb { get; set; } = 200;
        public string? ChartBaseUrl { get; set; }
        public bool EnableBaleChat { get; set; } = true;
        public string? BaleBotToken { get; set; }
        public string? BaleBotGroupId { get; set; }
        public bool EnableTelegramChat { get; set; }
        public string? TelegramBotToken { get; set; }
        public string? TelegramGroupId { get; set; }
        public bool EnableEitaaChat { get; set; }
        public string? EitaaBotToken { get; set; }
        public string? EitaaGroupId { get; set; }
        public bool EnableBaleNotifications { get; set; } = true;
        public bool EnableTelegramNotifications { get; set; } = true;
        public bool EnableEitaaNotifications { get; set; } = true;
        public string? BaleSafirApiKey { get; set; }
        public string? ChatWelcomeMessage { get; set; }
        public bool ChatEnableAutoMessage { get; set; }
        public int ChatAutoMessageSeconds { get; set; } = 60;
        public bool ChatPhoneRequired { get; set; }
        public string? ChatOperatorImageUrl { get; set; }
        public bool ChatGreetingEnabled { get; set; } = true;
        public string? ChatGreetingText { get; set; }
        public bool ChatEnableAiAssistant { get; set; }
        public string? ChatAiProvider { get; set; }
        public string? ChatAiApiUrl { get; set; }
        public string? ChatAiModel { get; set; }
        public string? ChatAiApiKey { get; set; }
        public string? ChatAiSystemPrompt { get; set; }
        public bool ChatAiGuestEnabled { get; set; }
        public int ChatAiDailyLimit { get; set; } = 3;
        public int ChatAiMaxQuestionChars { get; set; } = 300;
        public int ChatAiMaxTokens { get; set; } = 450;
        public double ChatAiTemperature { get; set; } = 0.6;
        public int ChatAiSessionLimit { get; set; } = 12;
        public string? ChatAiBlockKeywords { get; set; }
        public bool RequireReviewApproval { get; set; } = true;

        public List<SiteHomeModuleSetting> HomeModules { get; set; } = new();
    }

    public sealed class SiteHomeModuleSetting
    {
        [System.ComponentModel.DataAnnotations.Key]
        public int Id { get; set; }
        public int SiteSettingsId { get; set; }
        public int Key { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int SortOrder { get; set; }
    }
}
