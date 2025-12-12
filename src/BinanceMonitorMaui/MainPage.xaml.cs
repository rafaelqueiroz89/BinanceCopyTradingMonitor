using BinanceMonitorMaui.Models;
using BinanceMonitorMaui.Services;
using Plugin.LocalNotification;
using System.Collections.ObjectModel;

namespace BinanceMonitorMaui;

public partial class MainPage : ContentPage
{
    private readonly WebSocketService _webSocket;
    private readonly ObservableCollection<TraderGroup> _groupedPositions = new();
    private const string UrlKey = "websocket_url";
    private const string TokenKey = "websocket_token";
    private const string DefaultUrl = "ws://192.168.1.100:8765/";
    private bool _isConnecting;
    private int _notificationId = 100;

    public MainPage()
    {
        InitializeComponent();
        
        _webSocket = new WebSocketService();
        PositionsCollection.ItemsSource = _groupedPositions;

        _webSocket.OnConnectionStatusChanged += OnConnectionChanged;
        _webSocket.OnPositionsUpdated += OnPositionsReceived;
        _webSocket.OnTotalsUpdated += OnTotalsReceived;
        _webSocket.OnAlert += OnAlertReceived;
        _webSocket.OnQuickGainer += OnQuickGainerReceived;

        Dispatcher.Dispatch(async () => await ConnectToSavedUrl());
    }

    private void OnConnectionChanged(bool connected, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _isConnecting = false;
            
            if (connected)
            {
                var hasToken = !string.IsNullOrEmpty(Preferences.Get(TokenKey, ""));
                var statusText = hasToken ? "Connected 🔒" : "Connected";
                StatusLabel.Text = statusText;
                StatusLabel.TextColor = Color.FromArgb("#4ade80");
                
                // Start foreground service and update notification
                StartBackgroundService();
                UpdateServiceNotification(statusText);
            }
            else
            {
                var statusText = message == "Auth failed" ? "Auth Failed" : "Disconnected";
                StatusLabel.Text = statusText;
                StatusLabel.TextColor = Color.FromArgb("#e94560");
                PositionCountLabel.Text = "";
                _groupedPositions.Clear();
                TotalPnLLabel.Text = "0.00";
                TotalPnLLabel.TextColor = Colors.White;
                TotalPnLPercentLabel.Text = "0.00%";
                TotalPnLPercentLabel.TextColor = Colors.White;
                
                // Update notification to show disconnected
                UpdateServiceNotification(statusText);
                
                if (message != "Auth failed")
                {
                    Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(5), async () => await ConnectToSavedUrl());
                }
            }
        });
    }

    private void StartBackgroundService()
    {
#if ANDROID
        MainActivity.Instance?.StartForegroundService();
#endif
    }

    private void StopBackgroundService()
    {
#if ANDROID
        MainActivity.Instance?.StopForegroundService();
#endif
    }

    private void UpdateServiceNotification(string status)
    {
#if ANDROID
        WebSocketForegroundService.UpdateNotificationStatus(status);
#endif
    }

    private void OnAlertReceived(string title, string message, bool isProfit)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var icon = isProfit ? "💰" : "⚠️";
            await DisplayAlert($"{icon} {title}", message, "OK");
        });
    }

    private void OnQuickGainerReceived(QuickGainerAlert alert)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Check quiet hours (00:00 - 08:00)
            var hour = DateTime.Now.Hour;
            var isQuietHours = hour >= 0 && hour < 8;
            
            var icon = alert.IsExplosion ? "🚀" : "🔥";
            var title = alert.IsExplosion ? "EXPLOSION!" : "Growing Fast";
            
            var notification = new NotificationRequest
            {
                NotificationId = _notificationId++,
                Title = $"{icon} {title}",
                Description = $"{alert.Trader} | {alert.Symbol}\nGrew {alert.Growth:+0.00}% → now at {alert.PnLPercentage:+0.00}%",
                CategoryType = NotificationCategoryType.Alarm,
                Android = new Plugin.LocalNotification.AndroidOption.AndroidOptions
                {
                    ChannelId = "quick_gainer_urgent",
                    Priority = isQuietHours 
                        ? Plugin.LocalNotification.AndroidOption.AndroidPriority.Low 
                        : Plugin.LocalNotification.AndroidOption.AndroidPriority.Max,
                    VisibilityType = Plugin.LocalNotification.AndroidOption.AndroidVisibilityType.Public,
                    LedColor = alert.IsExplosion ? Android.Graphics.Color.Red : Android.Graphics.Color.Orange,
                    TimeoutAfter = TimeSpan.FromSeconds(60),
                    VibrationPattern = isQuietHours ? null : new long[] { 0, 500, 200, 500, 200, 500 },
                    AutoCancel = true,
                    Ongoing = false
                }
            };
            
            await LocalNotificationCenter.Current.Show(notification);
            
            // Only wake screen outside quiet hours
            if (!isQuietHours)
            {
                WakeScreen();
            }
        });
    }
    
    private void WakeScreen()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var powerManager = context.GetSystemService(Android.Content.Context.PowerService) as Android.OS.PowerManager;
            
            if (powerManager != null && !powerManager.IsInteractive)
            {
                var wakeLock = powerManager.NewWakeLock(
                    Android.OS.WakeLockFlags.ScreenBright | 
                    Android.OS.WakeLockFlags.AcquireCausesWakeup,
                    "BinanceMonitor::AlertWake");
                wakeLock?.Acquire(5000); // Wake for 5 seconds
            }
        }
        catch { }
#endif
    }

    private void OnPositionsReceived(List<Position> positions)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var grouped = positions.GroupBy(p => p.Trader).ToDictionary(g => g.Key, g => g.ToList());
            var incomingTraders = grouped.Keys.ToHashSet();

            for (int i = _groupedPositions.Count - 1; i >= 0; i--)
            {
                if (!incomingTraders.Contains(_groupedPositions[i].TraderName))
                    _groupedPositions.RemoveAt(i);
            }

            foreach (var kvp in grouped)
            {
                var traderName = kvp.Key;
                var traderPositions = kvp.Value;
                var existingGroup = _groupedPositions.FirstOrDefault(g => g.TraderName == traderName);

                if (existingGroup != null)
                {
                    var positionKeys = traderPositions.Select(p => p.Symbol).ToHashSet();
                    
                    for (int i = existingGroup.Count - 1; i >= 0; i--)
                    {
                        if (!positionKeys.Contains(existingGroup[i].Symbol))
                            existingGroup.RemoveAt(i);
                    }

                    foreach (var pos in traderPositions)
                    {
                        var existing = existingGroup.FirstOrDefault(p => p.Symbol == pos.Symbol);
                        if (existing != null)
                        {
                            existing.UpdateFrom(pos);
                        }
                        else
                        {
                            existingGroup.Add(pos);
                        }
                    }

                    existingGroup.UpdateTotalPnL();
                }
                else
                {
                    _groupedPositions.Add(new TraderGroup(traderName, traderPositions));
                }
            }

            PositionCountLabel.Text = $"({positions.Count})";
        });
    }

    private void OnTotalsReceived(decimal totalPnL, decimal totalPnLPercentage)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TotalPnLLabel.Text = $"{totalPnL:+0.00;-0.00}";
            TotalPnLLabel.TextColor = totalPnL < 0 ? Color.FromArgb("#e94560") : Color.FromArgb("#4ade80");
            
            TotalPnLPercentLabel.Text = $"{totalPnLPercentage:+0.00;-0.00}%";
            TotalPnLPercentLabel.TextColor = totalPnLPercentage < 0 ? Color.FromArgb("#e94560") : Color.FromArgb("#4ade80");
        });
    }

    private async Task ConnectToSavedUrl()
    {
        if (_isConnecting || _webSocket.IsConnected) return;
        
        _isConnecting = true;
        var url = Preferences.Get(UrlKey, DefaultUrl);
        var token = Preferences.Get(TokenKey, "");
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "Connecting...";
            StatusLabel.TextColor = Color.FromArgb("#f59e0b");
            UpdateServiceNotification("Connecting...");
        });
        
        await _webSocket.ConnectAsync(url, string.IsNullOrEmpty(token) ? null : token);
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        var currentUrl = Preferences.Get(UrlKey, DefaultUrl);
        var currentToken = Preferences.Get(TokenKey, "");
        
        var action = await DisplayActionSheet("Settings", "Cancel", null, "Change Server URL", "Change Token", "Clear Token");
        
        switch (action)
        {
            case "Change Server URL":
                var newUrl = await DisplayPromptAsync(
                    "Server URL",
                    "WebSocket URL:",
                    initialValue: currentUrl,
                    keyboard: Keyboard.Url);
                    
                if (!string.IsNullOrEmpty(newUrl) && newUrl != currentUrl)
                {
                    Preferences.Set(UrlKey, newUrl);
                    await ReconnectAsync();
                }
                break;
                
            case "Change Token":
                var newToken = await DisplayPromptAsync(
                    "Auth Token",
                    "Enter token (leave empty for no auth):",
                    initialValue: currentToken);
                    
                if (newToken != null && newToken != currentToken)
                {
                    Preferences.Set(TokenKey, newToken);
                    await ReconnectAsync();
                }
                break;
                
            case "Clear Token":
                Preferences.Set(TokenKey, "");
                await ReconnectAsync();
                break;
        }
    }
    
    private async Task ReconnectAsync()
    {
        StopBackgroundService();
        await _webSocket.DisconnectAsync();
        _isConnecting = false;
        await ConnectToSavedUrl();
    }
}
