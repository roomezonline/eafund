namespace MiniBank.Web.Components.DesignSystem;

public enum DsToastType
{
    Success,
    Info,
    Warning,
    Error
}

public class DsToastModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Message { get; set; } = string.Empty;
    public DsToastType Type { get; set; } = DsToastType.Success;
    public int Duration { get; set; } = 5000;
    public bool ShowProgressBar { get; set; } = true;
    public bool ShowCloseButton { get; set; } = true;
    public bool AutoClose { get; set; } = true;
    public bool Show { get; set; } = false;
    public Func<Task>? OnClick { get; set; }
}

public sealed class DsToastService
{
    public event Func<DsToastModel, Task>? OnShow;
    public event Func<string, Task>? OnHide;
    public event Func<Task>? OnClear;

    public async Task ShowAsync(string message, DsToastType type = DsToastType.Success, int duration = 5000, bool showProgressBar = true, bool showCloseButton = true, bool autoClose = true, Func<Task>? onClick = null)
    {
        var toast = new DsToastModel
        {
            Message = message,
            Type = type,
            Duration = duration,
            ShowProgressBar = showProgressBar,
            ShowCloseButton = showCloseButton,
            AutoClose = autoClose,
            OnClick = onClick
        };

        if (OnShow != null)
        {
            await OnShow.Invoke(toast);
        }
    }

    public Task SuccessAsync(string message, int duration = 5000, bool showProgressBar = true, bool showCloseButton = true, bool autoClose = true, Func<Task>? onClick = null)
        => ShowAsync(message, DsToastType.Success, duration, showProgressBar, showCloseButton, autoClose, onClick);

    public Task InfoAsync(string message, int duration = 5000, bool showProgressBar = true, bool showCloseButton = true, bool autoClose = true, Func<Task>? onClick = null)
        => ShowAsync(message, DsToastType.Info, duration, showProgressBar, showCloseButton, autoClose, onClick);

    public Task WarningAsync(string message, int duration = 5000, bool showProgressBar = true, bool showCloseButton = true, bool autoClose = true, Func<Task>? onClick = null)
        => ShowAsync(message, DsToastType.Warning, duration, showProgressBar, showCloseButton, autoClose, onClick);

    public Task ErrorAsync(string message, int duration = 5000, bool showProgressBar = true, bool showCloseButton = true, bool autoClose = true, Func<Task>? onClick = null)
        => ShowAsync(message, DsToastType.Error, duration, showProgressBar, showCloseButton, autoClose, onClick);

    // Synchronous methods for backward compatibility
    public void Show(string message, DsToastType type = DsToastType.Success, int duration = 5000)
        => _ = ShowAsync(message, type, duration);

    public void Success(string message, int duration = 5000) => Show(message, DsToastType.Success, duration);
    public void Info(string message, int duration = 5000) => Show(message, DsToastType.Info, duration);
    public void Warning(string message, int duration = 5000) => Show(message, DsToastType.Warning, duration);
    public void Error(string message, int duration = 5000) => Show(message, DsToastType.Error, duration);

    public async Task HideAsync(string toastId)
    {
        if (OnHide != null)
        {
            await OnHide.Invoke(toastId);
        }
    }

    public async Task ClearAllAsync()
    {
        if (OnClear != null)
        {
            await OnClear.Invoke();
        }
    }
}
