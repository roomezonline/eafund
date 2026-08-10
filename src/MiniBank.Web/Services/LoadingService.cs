using System;

namespace MiniBank.Web.Services
{
    public class LoadingService
    {
        public event Action? StateChanged;

        public bool IsShown { get; private set; }
        public string Title { get; private set; } = "صندوق امام علی (ع)";
        public string Subtitle { get; private set; } = "در حال بارگذاری...";
        public string Icon { get; private set; } = "fa-solid fa-vault";

        public void Show(string? title = null, string? subtitle = null, string? icon = null)
        {
            if (!string.IsNullOrWhiteSpace(title)) Title = title!;
            if (!string.IsNullOrWhiteSpace(subtitle)) Subtitle = subtitle!;
            if (!string.IsNullOrWhiteSpace(icon)) Icon = icon!;
            IsShown = true;
            StateChanged?.Invoke();
        }

        public void Hide()
        {
            IsShown = false;
            StateChanged?.Invoke();
        }
    }
}
