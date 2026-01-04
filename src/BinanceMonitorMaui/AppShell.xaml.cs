using BinanceMonitorMaui.Services;
using Plugin.LocalNotification;

namespace BinanceMonitorMaui;

public partial class AppShell : Shell
{
    public static AppShell? Instance { get; private set; }
    
    private AlertService? _alertService;
    private WebSocketService? _webSocketService;
    
    public AppShell()
    {
        InitializeComponent();
        Instance = this;
    }
    
    public void SetAlertService(AlertService alertService)
    {
        _alertService = alertService;
        UpdateQuietHoursLabel();
    }
    
    public void SetWebSocketService(WebSocketService webSocketService)
    {
        _webSocketService = webSocketService;
    }
    
    public WebSocketService? GetWebSocketService() => _webSocketService ?? WebSocketService.Instance;
    
    public void UpdateConnectionStatus(bool connected, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FlyoutStatusLabel.Text = message;
            FlyoutStatusLabel.TextColor = connected 
                ? Color.FromArgb("#4ade80") 
                : Color.FromArgb("#e94560");
        });
    }
    
    public void UpdateQuietHoursLabel()
    {
        if (_alertService == null) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_alertService.QuietHoursEnabled)
            {
                QuietHoursLabel.Text = $"Quiet Hours ({_alertService.QuietStartHour:00}:00-{_alertService.QuietEndHour:00}:00)";
            }
            else
            {
                QuietHoursLabel.Text = "Quiet Hours (Off)";
            }
        });
    }
    
    private async void OnFlyoutRefreshClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        if (Shell.Current.CurrentPage is MainPage mainPage)
        {
            await mainPage.RefreshPagesAsync();
        }
    }
    
    private async void OnFlyoutPortfolioClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        if (Shell.Current.CurrentPage is MainPage mainPage)
        {
            await mainPage.PortfolioAnalysisAsync();
        }
    }

    private async void OnFlyoutScrapeGrowthClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        if (Shell.Current.CurrentPage is MainPage mainPage)
        {
            await mainPage.ScrapeGrowthAsync();
        }
    }

    private async void OnFlyoutRestartClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        if (Shell.Current.CurrentPage is MainPage mainPage)
        {
            await mainPage.RestartChromeAsync();
        }
    }
    
    private void OnFlyoutTestNotificationClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        
        try
        {
            var notification = new NotificationRequest
            {
                NotificationId = new Random().Next(10000, 99999),
                Title = "🔔 Test Notification",
                Description = "Notifications are working! You'll receive alerts here.",
                CategoryType = NotificationCategoryType.Status,
                Android = new Plugin.LocalNotification.AndroidOption.AndroidOptions
                {
                    Priority = Plugin.LocalNotification.AndroidOption.AndroidPriority.High,
                    ChannelId = "binance_alerts"
                }
            };
            
            LocalNotificationCenter.Current.Show(notification);
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current.CurrentPage is Page page)
                {
                    await page.DisplayAlert("Error", $"Could not send notification: {ex.Message}", "OK");
                }
            });
        }
    }
    
    private async void OnFlyoutQuietHoursClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        if (Shell.Current.CurrentPage is MainPage mainPage)
        {
            await mainPage.ShowQuietHoursMenuAsync();
        }
    }
    
    private async void OnFlyoutServerUrlClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        if (Shell.Current.CurrentPage is MainPage mainPage)
        {
            await mainPage.ChangeServerUrlAsync();
        }
    }
    
    private async void OnFlyoutTokenClicked(object? sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        if (Shell.Current.CurrentPage is MainPage mainPage)
        {
            await mainPage.ChangeTokenAsync();
        }
    }
}
