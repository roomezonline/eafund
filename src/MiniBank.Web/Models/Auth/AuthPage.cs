namespace MiniBank.Web.Models.Auth
{
    public class AuthPage
    {
        public long PageId { get; set; }
        public string PageKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
