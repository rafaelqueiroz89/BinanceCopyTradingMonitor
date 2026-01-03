using BinanceMonitorMaui.Models;
using BinanceMonitorMaui.Services;
using BinanceMonitorMaui.Views;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;

namespace BinanceMonitorMaui;

public partial class MainPage : ContentPage
{
    private readonly WebSocketService _webSocket;
    private readonly AlertService _alertService;
    private readonly ObservableCollection<TraderGroup> _groupedPositions = new();
    private readonly ObservableCollection<GrowthUpdate> _portfolioGrowthUpdates = new();
    private readonly ObservableCollection<Withdrawal> _portfolioWithdrawals = new();
    private PortfolioData _portfolio = new();
    private LineChartView _chartDrawable = new();
    private const string UrlKey = "websocket_url";
    private const string TokenKey = "websocket_token";
    private const string DefaultUrl = "ws://192.168.1.100:8765/";
    private bool _isConnecting;
    private DateTime? _connectionStartTime;
    private IDispatcherTimer? _connectionTimer;

	public MainPage()
	{
		InitializeComponent();
        
        _webSocket = WebSocketService.Instance;
        _alertService = new AlertService();
        PositionsCollection.ItemsSource = _groupedPositions;

        _webSocket.OnConnectionStatusChanged += OnConnectionChanged;
        _webSocket.OnPositionsUpdated += OnPositionsReceived;
        _webSocket.OnTotalsUpdated += OnTotalsReceived;
        _webSocket.OnAnalysisResult += OnAnalysisResultReceived;
        _webSocket.OnPortfolioAnalysisResult += OnPortfolioAnalysisResultReceived;
        _webSocket.OnTPSLClickResult += OnTPSLClickResultReceived;
        _webSocket.OnClosePositionResult += OnClosePositionResultReceived;
        _webSocket.OnAvgPnLResult += OnAvgPnLResultReceived;
        _webSocket.OnPortfolioDataReceived += OnPortfolioDataReceived;
        _webSocket.OnPortfolioUpdateResult += OnPortfolioUpdateResult;
        
        _alertService.OnAlertTriggered += OnCustomAlertTriggered;
        
        // Register services with AppShell for flyout menu
        AppShell.Instance?.SetAlertService(_alertService);
        AppShell.Instance?.SetWebSocketService(_webSocket);
        
        // Setup portfolio collections
        PortfolioGrowthUpdatesCollection.ItemsSource = _portfolioGrowthUpdates;
        PortfolioWithdrawalsCollection.ItemsSource = _portfolioWithdrawals;

        Dispatcher.Dispatch(async () => await ConnectToSavedUrl());
    }
    
    private void OnCustomAlertTriggered(string symbol, string message)
    {
        // Notifications are now sent via AlertService.SendNotification()
        // No popup needed - the notification will appear in the tray
        System.Diagnostics.Debug.WriteLine($"[Alert] {symbol}: {message}");
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
                
                // Green dot for connected
                StatusDot.Fill = Color.FromArgb("#4ade80");
                
                // Start tracking connection time
                _connectionStartTime = DateTime.Now;
                StartConnectionTimer();
                
                StartBackgroundService();
                UpdateServiceNotification(statusText);
                
                // Update flyout status
                AppShell.Instance?.UpdateConnectionStatus(true, statusText);
            }
            else
            {
                var statusText = message == "Auth failed" ? "Auth Failed" : "Disconnected";
                
                // Red dot for disconnected
                StatusDot.Fill = Color.FromArgb("#e94560");
                
                // Stop tracking connection time
                StopConnectionTimer();
                _connectionStartTime = null;
                LastUpdateLabel.Text = "";
                
                PositionCountLabel.Text = "";
                _groupedPositions.Clear();
                TotalPnLLabel.Text = "0.00";
                TotalPnLLabel.TextColor = Colors.White;
                TotalPnLPercentLabel.Text = "0.00%";
                TotalPnLPercentLabel.TextColor = Colors.White;
                
                UpdateServiceNotification(statusText);
                
                // Update flyout status
                AppShell.Instance?.UpdateConnectionStatus(false, statusText);
                
                if (message != "Auth failed")
                {
                    Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(5), async () => await ConnectToSavedUrl());
                }
            }
        });
    }
    
    private void StartConnectionTimer()
    {
        StopConnectionTimer();
        
        _connectionTimer = Dispatcher.CreateTimer();
        _connectionTimer.Interval = TimeSpan.FromSeconds(1);
        _connectionTimer.Tick += (s, e) => UpdateConnectionTime();
        _connectionTimer.Start();
        
        // Update immediately
        UpdateConnectionTime();
    }
    
    private void StopConnectionTimer()
    {
        _connectionTimer?.Stop();
        _connectionTimer = null;
    }
    
    private void UpdateConnectionTime()
    {
        if (_connectionStartTime == null) return;
        
        var elapsed = DateTime.Now - _connectionStartTime.Value;
        
        string timeText;
        if (elapsed.TotalHours >= 1)
        {
            timeText = $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m";
        }
        else if (elapsed.TotalMinutes >= 1)
        {
            timeText = $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        }
        else
        {
            timeText = $"{elapsed.Seconds}s";
        }
        
        LastUpdateLabel.Text = timeText;
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
                    var positionKeys = traderPositions.Select(p => p.UniqueKey).ToHashSet();
                    
                    for (int i = existingGroup.Count - 1; i >= 0; i--)
                    {
                        if (!positionKeys.Contains(existingGroup[i].UniqueKey))
                            existingGroup.RemoveAt(i);
                    }

                    foreach (var pos in traderPositions)
                    {
                        _alertService.EnsureDefaultAlert(pos.UniqueKey);
                        pos.AlertIndicator = _alertService.GetAlertIndicator(pos.UniqueKey);
                        _alertService.CheckAlerts(pos);
                        
                        var existing = existingGroup.FirstOrDefault(p => p.UniqueKey == pos.UniqueKey);
                        if (existing != null)
                        {
                            existing.UpdateFrom(pos);
                            existing.AlertIndicator = pos.AlertIndicator;
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
                    foreach (var pos in traderPositions)
                    {
                        _alertService.EnsureDefaultAlert(pos.UniqueKey);
                        pos.AlertIndicator = _alertService.GetAlertIndicator(pos.UniqueKey);
                        _alertService.CheckAlerts(pos);
                    }
                    _groupedPositions.Add(new TraderGroup(traderName, traderPositions));
                }
            }

            PositionCountLabel.Text = $"{positions.Count} positions";
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
            // Orange dot for connecting
            StatusDot.Fill = Color.FromArgb("#f59e0b");
            UpdateServiceNotification("Connecting...");
            AppShell.Instance?.UpdateConnectionStatus(false, "Connecting...");
        });
        
        await _webSocket.ConnectAsync(url, string.IsNullOrEmpty(token) ? null : token);
    }

    private void OnSwipeHintTapped(object? sender, EventArgs e)
    {
        // Open the flyout menu when tapping the hamburger icon
        Shell.Current.FlyoutIsPresented = true;
    }
    
    // Public methods for AppShell to call
    public async Task RefreshPagesAsync()
    {
        if (!_webSocket.IsConnected)
        {
            await DisplayAlert("Not Connected", "Connect to server first", "OK");
            return;
        }
        
        await _webSocket.SendRefreshAsync();
        ShowToast("Refreshing pages...");
    }
    
    public async Task PortfolioAnalysisAsync()
    {
        if (!_webSocket.IsConnected)
        {
            await DisplayAlert("Not Connected", "Connect to server first", "OK");
            return;
        }
        
        var positionCount = _groupedPositions.Sum(g => g.Count);
        if (positionCount == 0)
        {
            await DisplayAlert("No Positions", "No positions to analyze", "OK");
            return;
        }
        
        ShowLoading($"Analyzing {positionCount} positions...\nThis may take a moment");
        await _webSocket.SendPortfolioAnalysisAsync();
    }
    
    public async Task RestartChromeAsync()
    {
        if (!_webSocket.IsConnected)
        {
            await DisplayAlert("Not Connected", "Connect to server first", "OK");
            return;
        }
        
        bool confirm = await DisplayAlert("Restart Chrome", 
            "This will kill Chrome and restart the scraper. Continue?", 
            "Restart", "Cancel");
            
        if (!confirm) return;
        
        await _webSocket.SendRestartAsync();
        ShowToast("Restarting Chrome...");
    }
    
    public async Task ChangeServerUrlAsync()
    {
        var currentUrl = Preferences.Get(UrlKey, DefaultUrl);
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
    }
    
    public async Task ChangeTokenAsync()
    {
        var currentToken = Preferences.Get(TokenKey, "");
        
        var action = await DisplayActionSheet("Auth Token", "Cancel", null,
            "Set/Change Token",
            "Clear Token");
            
        if (action == "Set/Change Token")
        {
            var newToken = await DisplayPromptAsync(
                "Auth Token",
                "Enter token:",
                initialValue: currentToken);
                
            if (newToken != null && newToken != currentToken)
            {
                Preferences.Set(TokenKey, newToken);
                await ReconnectAsync();
            }
        }
        else if (action == "Clear Token")
        {
            Preferences.Set(TokenKey, "");
            await ReconnectAsync();
        }
    }
    
    public async Task ShowQuietHoursMenuAsync()
    {
        var enabled = _alertService.QuietHoursEnabled;
        var start = _alertService.QuietStartHour;
        var end = _alertService.QuietEndHour;
        
        var statusText = enabled ? "ON" : "OFF";
        var currentRange = $"{start:00}:00 - {end:00}:00";
        
        var action = await DisplayActionSheet(
            $"Quiet Hours: {statusText}\n({currentRange})",
            "Cancel",
            null,
            enabled ? "🔔 Turn Off" : "🔕 Turn On",
            "⏰ Set Start Time",
            "⏰ Set End Time",
            "🌙 Night (00:00-09:00)",
            "🌃 Late Night (23:00-07:00)");
            
        switch (action)
        {
            case "🔔 Turn Off":
                _alertService.QuietHoursEnabled = false;
                ShowToast("Quiet hours disabled - alerts active 24/7");
                break;
                
            case "🔕 Turn On":
                _alertService.QuietHoursEnabled = true;
                ShowToast($"Quiet hours enabled: {start:00}:00-{end:00}:00");
                break;
                
            case "⏰ Set Start Time":
                var startInput = await DisplayPromptAsync(
                    "Quiet Hours Start",
                    "Enter hour (0-23):",
                    initialValue: start.ToString(),
                    keyboard: Keyboard.Numeric);
                if (int.TryParse(startInput, out int newStart) && newStart >= 0 && newStart <= 23)
                {
                    _alertService.QuietStartHour = newStart;
                    ShowToast($"Quiet hours: {newStart:00}:00-{end:00}:00");
                }
                break;
                
            case "⏰ Set End Time":
                var endInput = await DisplayPromptAsync(
                    "Quiet Hours End",
                    "Enter hour (0-23):",
                    initialValue: end.ToString(),
                    keyboard: Keyboard.Numeric);
                if (int.TryParse(endInput, out int newEnd) && newEnd >= 0 && newEnd <= 23)
                {
                    _alertService.QuietEndHour = newEnd;
                    ShowToast($"Quiet hours: {start:00}:00-{newEnd:00}:00");
                }
                break;
                
            case "🌙 Night (00:00-09:00)":
                _alertService.QuietStartHour = 0;
                _alertService.QuietEndHour = 9;
                _alertService.QuietHoursEnabled = true;
                ShowToast("Quiet hours set: 00:00-09:00");
                break;
                
            case "🌃 Late Night (23:00-07:00)":
                _alertService.QuietStartHour = 23;
                _alertService.QuietEndHour = 7;
                _alertService.QuietHoursEnabled = true;
                ShowToast("Quiet hours set: 23:00-07:00");
                break;
        }
        
        // Update the flyout label
        AppShell.Instance?.UpdateQuietHoursLabel();
    }
    
    private async Task ReconnectAsync()
    {
        StopBackgroundService();
        await _webSocket.DisconnectAsync();
        _isConnecting = false;
        await ConnectToSavedUrl();
    }

    private void ShowLoading(string text = "Analyzing...")
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadingLabel.Text = text;
            LoadingOverlay.IsVisible = true;
        });
    }
    
    private void HideLoading()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadingOverlay.IsVisible = false;
        });
    }
    
    private Position? FindPositionByKey(string uniqueKey)
    {
        foreach (var group in _groupedPositions)
        {
            var pos = group.FirstOrDefault(p => p.UniqueKey == uniqueKey);
            if (pos != null) return pos;
        }
        return null;
    }
    
    private async void OnPositionMenuClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string positionKey)
        {
            var position = FindPositionByKey(positionKey);
            if (position == null) return;
            
            var existingAlerts = _alertService.GetAlerts(positionKey);
            var alertsInfo = existingAlerts.Count > 0 
                ? $" ({existingAlerts.Count} alerts)"
                : "";
            
            var posInfo = $"{position.Symbol} ({position.Side})";
            
            var action = await DisplayActionSheet(
                $"📊 {posInfo}{alertsInfo}",
                "Cancel",
                null,
                "🤖 AI Analysis",
                "🔔 Set Alert",
                "📈 Setup TP/SL",
                "❌ Close Position");
                
            switch (action)
            {
                case "🤖 AI Analysis":
                    await DoAIAnalysis(position);
                    break;
                case "🔔 Set Alert":
                    await ShowAlertMenu(positionKey, position);
                    break;
                case "📈 Setup TP/SL":
                    if (!_webSocket.IsConnected)
                    {
                        await DisplayAlert("Not Connected", "Connect to server first", "OK");
                        return;
                    }
                    ShowLoading($"Opening TP/SL for {position.Symbol}...");
                    await _webSocket.SendClickTPSLAsync(position.Trader, position.Symbol, position.Size);
                    break;
                case "❌ Close Position":
                    if (!_webSocket.IsConnected)
                    {
                        await DisplayAlert("Not Connected", "Connect to server first", "OK");
                        return;
                    }
                    // Click the Close Position link (column 10)
                    ShowLoading($"Opening Close Position for {position.Symbol}...");
                    await _webSocket.SendClosePositionAsync(position.Trader, position.Symbol, position.Size);
                    HideLoading();
                    
                    // Wait a moment for modal to open
                    await Task.Delay(500);
                    
                    // Then ask for confirmation
                    var keepOpen = await DisplayAlert("❌ Close Position Opened", 
                        $"Close Position dialog is now open for {position.Symbol} ({position.Size}).\n\nDo you want to keep it open?", 
                        "Yes, Keep Open", "No, Close It");
                    
                    if (!keepOpen)
                    {
                        // Close the modal
                        await _webSocket.SendCloseModalAsync(position.Trader);
                    }
                    break;
            }
        }
    }
    
    private async Task DoAIAnalysis(Position position)
    {
        if (!_webSocket.IsConnected)
        {
            await DisplayAlert("Not Connected", "Connect to server first", "OK");
            return;
        }
        
        ShowLoading($"Analyzing {position.Symbol}...");
        await _webSocket.SendAnalyzeAsync(position.Symbol);
    }
    
    private async Task ShowAlertMenu(string positionKey, Position position)
    {
        var existingAlerts = _alertService.GetAlerts(positionKey);
        var alertsInfo = existingAlerts.Count > 0 
            ? $"\n\nActive: {string.Join(", ", existingAlerts.Select(a => a.Description))}"
            : "\n\nDefault: PnL > +5%";
        
        var posInfo = $"{position.Symbol} ({position.Side})";
        
        var action = await DisplayActionSheet(
            $"🔔 {posInfo}{alertsInfo}",
            "Cancel",
            null,
            "✏️ Custom PnL %",
            "📈 PnL > +10%",
            "📈 PnL > +20%",
            "📉 PnL < -10%",
            "📉 PnL < -20%",
            "🔄 Reset triggered",
            "🗑️ Remove all");
            
        if (action == null || action == "Cancel") return;
        
        switch (action)
        {
            case "✏️ Custom PnL %":
                var input = await DisplayPromptAsync(
                    "Custom Alert",
                    "Alert when PnL reaches this % (use negative for below):",
                    initialValue: "5",
                    keyboard: Keyboard.Numeric);
                if (decimal.TryParse(input, out var val))
                {
                    bool isAbove = val >= 0;
                    _alertService.AddAlert(positionKey, "pnl_percent", val, isAbove);
                    await DisplayAlert("✅ Alert Added", $"Will alert when {posInfo} PnL {(isAbove ? ">" : "<")} {val}%", "OK");
                }
                break;
            case "📈 PnL > +10%":
                _alertService.AddAlert(positionKey, "pnl_percent", 10, true);
                await DisplayAlert("✅ Alert Added", $"Will alert when {posInfo} PnL > +10%", "OK");
                break;
            case "📈 PnL > +20%":
                _alertService.AddAlert(positionKey, "pnl_percent", 20, true);
                await DisplayAlert("✅ Alert Added", $"Will alert when {posInfo} PnL > +20%", "OK");
                break;
            case "📉 PnL < -10%":
                _alertService.AddAlert(positionKey, "pnl_percent", -10, false);
                await DisplayAlert("✅ Alert Added", $"Will alert when {posInfo} PnL < -10%", "OK");
                break;
            case "📉 PnL < -20%":
                _alertService.AddAlert(positionKey, "pnl_percent", -20, false);
                await DisplayAlert("✅ Alert Added", $"Will alert when {posInfo} PnL < -20%", "OK");
                break;
            case "🔄 Reset triggered":
                _alertService.ResetTriggeredAlerts(positionKey);
                await DisplayAlert("✅ Reset", $"Triggered alerts for {posInfo} have been reset", "OK");
                break;
            case "🗑️ Remove all":
                _alertService.RemoveAllAlerts(positionKey);
                await DisplayAlert("✅ Removed", $"All alerts for {posInfo} removed", "OK");
                break;
        }
        
        UpdateAlertIndicators();
    }
    
    private void UpdateAlertIndicators()
    {
        foreach (var group in _groupedPositions)
        {
            foreach (var pos in group)
            {
                pos.AlertIndicator = _alertService.GetAlertIndicator(pos.UniqueKey);
            }
        }
    }
    
    private void OnAnalysisResultReceived(AnalysisResult result)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            HideLoading();
            
            var rec = result.Recommendation.ToUpper();
            var icon = rec switch
            {
                "SELL" or "CLOSE" => "🔴",
                "STAY" or "HOLD" => "🟢",
                "BUY" => "💚",
                _ => "🤖"
            };
            
            var confidenceBar = new string('█', result.Confidence / 10) + new string('░', 10 - result.Confidence / 10);
            var displayRec = rec == "CLOSE" ? "SELL" : rec;
            
            await DisplayAlert(
                $"{icon} {result.Symbol}",
                $"{result.Trader} | {result.CurrentPnlPercent}\n\n" +
                $"{displayRec} ({result.Confidence}%)\n\n" +
                $"{result.Summary}",
                "OK"
            );
        });
    }
    
    private void OnPortfolioAnalysisResultReceived(PortfolioAnalysisResult result)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                HideLoading();
                
                var insightsText = "";
                foreach (var insight in result.Insights)
                {
                    var rec = insight.Recommendation.ToUpper();
                    var icon = rec switch
                    {
                        "SELL" or "CLOSE" => "🔴",
                        "STAY" or "HOLD" => "🟢",
                        "BUY" => "💚",
                        _ => "⚪"
                    };
                    
                    insightsText += $"{icon} {insight.Symbol} ({insight.Trader})\n";
                    insightsText += $"   {insight.Insight}\n";
                    if (!string.IsNullOrEmpty(insight.MarketData))
                        insightsText += $"   {insight.MarketData}\n\n";
                }
                
                var totalPnLText = result.TotalPnL >= 0 ? $"+{result.TotalPnL:F2}" : $"{result.TotalPnL:F2}";
                
                var fullText = $"📊 {result.TotalPositions} positions | {totalPnLText} USDT\n\n" +
                               $"{result.Summary}\n\n" +
                               $"─────────────────\n" +
                               $"{insightsText}";
                
                await DisplayAlert("📈 Portfolio Analysis", fullText, "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to display analysis: {ex.Message}", "OK");
            }
        });
    }
    
    private void OnTPSLClickResultReceived(string trader, string symbol, bool success, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            HideLoading();
            
            if (!success)
            {
                await DisplayAlert("❌ TP/SL", $"Could not open TP/SL for {symbol}.\n\n{message}", "OK");
            }
            // Don't show success message since we'll show the confirmation dialog
        });
    }
    
    private void OnClosePositionResultReceived(string trader, string symbol, bool success, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            HideLoading();
            
            if (!success)
            {
                await DisplayAlert("❌ Close Position", $"Could not open Close Position for {symbol}.\n\n{message}", "OK");
            }
            // Don't show success message since we'll show the confirmation dialog
        });
    }
    
    private void OnAvgPnLResultReceived(AvgPnLResult result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!result.Success)
            {
                ShowToast("No data yet - keep app running");
                return;
            }
            
            var icon = result.AvgPnL >= 0 ? "📈" : "📉";
            var toastText = $"{icon} 1h Avg: {result.AvgPnL:+0.00;-0.00} ({result.AvgPnLPercent:+0.00;-0.00}%)";
            
            ShowToast(toastText);
        });
    }
    
    private async void ShowToast(string message)
    {
        // Create a toast label
        var toast = new Label
        {
            Text = message,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        
        // Add rounded corners using a Border
        var border = new Border
        {
            Content = toast,
            StrokeShape = new RoundRectangle { CornerRadius = 25 },
            BackgroundColor = Color.FromArgb("#333333"),
            Stroke = Colors.Transparent,
            Padding = new Thickness(24, 14),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(20, 0, 20, 80),
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Offset = new Point(0, 4),
                Radius = 8,
                Opacity = 0.3f
            }
        };
        
        // Create an overlay grid that spans all rows
        var overlay = new Grid
        {
            InputTransparent = true, // Allow taps to pass through
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        overlay.Children.Add(border);
        
        // Add to the main grid, spanning all rows
        if (Content is Grid mainGrid)
        {
            Grid.SetRowSpan(overlay, 3);
            mainGrid.Children.Add(overlay);
            
            // Animate in
            border.Opacity = 0;
            border.TranslationY = 50;
            
            await Task.WhenAll(
                border.FadeTo(1, 200),
                border.TranslateTo(0, 0, 200, Easing.CubicOut)
            );
            
            // Wait then fade out
            await Task.Delay(2000);
            
            await Task.WhenAll(
                border.FadeTo(0, 300),
                border.TranslateTo(0, 30, 300, Easing.CubicIn)
            );
            
            // Remove
            mainGrid.Children.Remove(overlay);
        }
    }
    
    private async void OnPositionTapped(object? sender, EventArgs e)
    {
        if (sender is View view && view.BindingContext is Position position)
        {
            if (!_webSocket.IsConnected) return;
            
            await _webSocket.SendGetAvgPnLAsync(position.UniqueKey);
        }
	}
    
    private void OnTabButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string tabName)
        {
            // Reset all tab states
            PositionsTab.IsVisible = false;
            PortfolioTab.IsVisible = false;
            WithdrawalsTab.IsVisible = false;

            // Reset all button styles
            PositionsTabButton.BackgroundColor = Color.FromArgb("#0f0f1a");
            PositionsTabButton.TextColor = Color.FromArgb("#888");
            PortfolioTabButton.BackgroundColor = Color.FromArgb("#0f0f1a");
            PortfolioTabButton.TextColor = Color.FromArgb("#888");
            WithdrawalsTabButton.BackgroundColor = Color.FromArgb("#0f0f1a");
            WithdrawalsTabButton.TextColor = Color.FromArgb("#888");

            if (tabName == "Positions")
            {
                PositionsTab.IsVisible = true;
                PositionsTabButton.BackgroundColor = Color.FromArgb("#16213e");
                PositionsTabButton.TextColor = Colors.White;
            }
            else if (tabName == "Portfolio")
            {
                PortfolioTab.IsVisible = true;
                PortfolioTabButton.BackgroundColor = Color.FromArgb("#16213e");
                PortfolioTabButton.TextColor = Colors.White;

                // Load portfolio when switching to portfolio tab
                if (_webSocket.IsConnected)
                {
                    _ = Task.Run(async () => await _webSocket.SendGetPortfolioAsync());
                }
            }
            else if (tabName == "Withdrawals")
            {
                WithdrawalsTab.IsVisible = true;
                WithdrawalsTabButton.BackgroundColor = Color.FromArgb("#16213e");
                WithdrawalsTabButton.TextColor = Colors.White;

                // Update withdrawals summary when switching to withdrawals tab
                UpdateWithdrawalsSummary();
            }
        }
    }
    
    private void OnPortfolioDataReceived(PortfolioData portfolio)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _portfolio = portfolio;
            UpdatePortfolioUI();
        });
    }
    
    private void OnPortfolioUpdateResult(bool success, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (success)
            {
                await _webSocket.SendGetPortfolioAsync();
            }
            else
            {
                await DisplayAlert("Error", message, "OK");
            }
        });
    }
    
    private void UpdatePortfolioUI()
    {
        PortfolioInitialValueLabel.Text = $"{_portfolio.InitialValue:0.00} USDT";
        PortfolioCurrentValueLabel.Text = $"{_portfolio.CurrentValue:0.00} USDT";
        PortfolioInitialDateLabel.Text = $"{_portfolio.InitialDate:yyyy-MM-dd}";

        var growth = _portfolio.TotalGrowth;
        var growthPercent = _portfolio.TotalGrowthPercent;
        PortfolioTotalGrowthLabel.Text = $"{growth:+0.00;-0.00} USDT ({growthPercent:+0.00;-0.00}%)";
        PortfolioTotalGrowthLabel.TextColor = growth >= 0 ? Color.FromArgb("#4ade80") : Color.FromArgb("#e94560");

        PortfolioTotalWithdrawalsLabel.Text = $"{_portfolio.TotalWithdrawals:0.00} USDT";

        // Update growth updates collection
        _portfolioGrowthUpdates.Clear();
        foreach (var update in _portfolio.GrowthUpdates.OrderByDescending(g => g.Date))
        {
            _portfolioGrowthUpdates.Add(update);
        }

        // Update withdrawals collection
        _portfolioWithdrawals.Clear();
        foreach (var withdrawal in _portfolio.Withdrawals.OrderByDescending(w => w.Date))
        {
            _portfolioWithdrawals.Add(withdrawal);
        }

        PortfolioNoGrowthUpdatesLabel.IsVisible = _portfolioGrowthUpdates.Count == 0;
        PortfolioNoWithdrawalsLabel.IsVisible = _portfolioWithdrawals.Count == 0;

        // Update chart
        UpdatePortfolioGrowthChart();

        // Update withdrawals summary
        UpdateWithdrawalsSummary();

        // Debug logging
        System.Diagnostics.Debug.WriteLine($"[UI] Portfolio updated: Initial={_portfolio.InitialValue}, Current={_portfolio.CurrentValue}, GrowthUpdates={_portfolio.GrowthUpdates.Count}, Withdrawals={_portfolio.Withdrawals.Count}");
        System.Diagnostics.Debug.WriteLine($"[UI] Collections: GrowthUpdates={_portfolioGrowthUpdates.Count}, Withdrawals={_portfolioWithdrawals.Count}");
    }
    
    private void UpdatePortfolioGrowthChart()
    {
        // Create chart data points starting from initial value and including current value
        var points = new List<(DateTime date, decimal value)>();

        // Always add initial value
        points.Add((_portfolio.InitialDate, _portfolio.InitialValue));

        // Add growth updates
        foreach (var update in _portfolio.GrowthUpdates.OrderBy(g => g.Date))
        {
            points.Add((update.Date, update.Value));
        }

        // Add current value if it's different from the last point
        if (points.Count == 0 || points.Last().value != _portfolio.CurrentValue)
        {
            points.Add((DateTime.Now, _portfolio.CurrentValue));
        }

        // Update chart drawable with data
        _chartDrawable.DataPoints = points;
        _chartDrawable.LineColor = Color.FromArgb("#4ade80");
        _chartDrawable.GridColor = Color.FromArgb("#333333");
        _chartDrawable.TextColor = Color.FromArgb("#888888");

        // Set the drawable to the GraphicsView
        PortfolioGrowthChartCanvas.Drawable = _chartDrawable;

        // Force a redraw
        PortfolioGrowthChartCanvas.Invalidate();
    }

    private void UpdateWithdrawalsSummary()
    {
        WithdrawalsTotalLabel.Text = $"{_portfolio.TotalWithdrawals:0.00} USDT";
        WithdrawalsCountLabel.Text = _portfolioWithdrawals.Count.ToString();
    }

    private async void OnPortfolioEditInitialValueClicked(object? sender, EventArgs e)
    {
        var valueStr = await DisplayPromptAsync(
            "Edit Initial Value",
            "Enter initial portfolio value (USDT):",
            initialValue: _portfolio.InitialValue.ToString("0.00"),
            keyboard: Keyboard.Numeric);
        
        if (string.IsNullOrEmpty(valueStr) || !decimal.TryParse(valueStr, out var value))
            return;
        
        var date = await ShowDatePickerDialog(_portfolio.InitialDate);
        await _webSocket.SendUpdateInitialValueAsync(value, date);
    }
    
    private async void OnPortfolioUpdateCurrentValueClicked(object? sender, EventArgs e)
    {
        var valueStr = await DisplayPromptAsync(
            "Update Current Value",
            "Enter current portfolio value (USDT):",
            initialValue: _portfolio.CurrentValue.ToString("0.00"),
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrEmpty(valueStr) || !decimal.TryParse(valueStr, out var value))
            return;

        // Add date picker for the update
        var date = await ShowDateTimePickerDialog(DateTime.Now);

        // Add a growth update with the selected date instead of just updating current value
        await _webSocket.SendAddGrowthUpdateAsync(value, "", date);
    }
    
    private async void OnPortfolioAddWithdrawalClicked(object? sender, EventArgs e)
    {
        var amountStr = await DisplayPromptAsync(
            "Add Withdrawal",
            "Enter withdrawal amount (USDT):",
            keyboard: Keyboard.Numeric);
        
        if (string.IsNullOrEmpty(amountStr) || !decimal.TryParse(amountStr, out var amount))
            return;
        
        var category = await DisplayActionSheet(
            "Select Category",
            "Cancel",
            null,
            "💳 Credit Card",
            "💶 EUR",
            "💵 BRL",
            "💱 Other Fiat",
            "🍔 Uber Eats",
            "🎫 Other Voucher");
        
        if (category == "Cancel" || string.IsNullOrEmpty(category))
            return;
        
        var categoryCode = category switch
        {
            "💳 Credit Card" => Withdrawal.Categories.CreditCard,
            "💶 EUR" => Withdrawal.Categories.FiatEur,
            "💵 BRL" => Withdrawal.Categories.FiatBrl,
            "💱 Other Fiat" => Withdrawal.Categories.FiatOther,
            "🍔 Uber Eats" => Withdrawal.Categories.VoucherUber,
            "🎫 Other Voucher" => Withdrawal.Categories.VoucherOther,
            _ => ""
        };
        
        var currency = categoryCode.StartsWith("fiat_") 
            ? (categoryCode == Withdrawal.Categories.FiatEur ? "EUR" : categoryCode == Withdrawal.Categories.FiatBrl ? "BRL" : "USD")
            : "USDT";
        
        // Add date picker for withdrawal
        var date = await ShowDatePickerDialog(DateTime.Now);
        
        var description = await DisplayPromptAsync(
            "Description",
            "Enter description:",
            initialValue: "",
            keyboard: Keyboard.Default);
        
        await _webSocket.SendAddWithdrawalAsync(amount, categoryCode, description ?? "", currency, date);
    }
    
    private async void OnPortfolioEditWithdrawalClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is Withdrawal withdrawal)
        {
            var amountStr = await DisplayPromptAsync(
                "Edit Withdrawal",
                "Enter withdrawal amount:",
                initialValue: withdrawal.Amount.ToString("0.00"),
                keyboard: Keyboard.Numeric);
            
            if (string.IsNullOrEmpty(amountStr) || !decimal.TryParse(amountStr, out var amount))
                return;
            
            var category = await DisplayActionSheet(
                "Select Category",
                "Cancel",
                null,
                "💳 Credit Card",
                "💶 EUR",
                "💵 BRL",
                "💱 Other Fiat",
                "🍔 Uber Eats",
                "🎫 Other Voucher");
            
            if (category == "Cancel" || string.IsNullOrEmpty(category))
                return;
            
            var categoryCode = category switch
            {
                "💳 Credit Card" => Withdrawal.Categories.CreditCard,
                "💶 EUR" => Withdrawal.Categories.FiatEur,
                "💵 BRL" => Withdrawal.Categories.FiatBrl,
                "💱 Other Fiat" => Withdrawal.Categories.FiatOther,
                "🍔 Uber Eats" => Withdrawal.Categories.VoucherUber,
                "🎫 Other Voucher" => Withdrawal.Categories.VoucherOther,
                _ => withdrawal.Category
            };
            
            var currency = categoryCode.StartsWith("fiat_") 
                ? (categoryCode == Withdrawal.Categories.FiatEur ? "EUR" : categoryCode == Withdrawal.Categories.FiatBrl ? "BRL" : "USD")
                : withdrawal.Currency;
            
            // Add date picker for withdrawal edit
            var date = await ShowDatePickerDialog(withdrawal.Date);
            
            var description = await DisplayPromptAsync(
                "Description",
                "Enter description:",
                initialValue: withdrawal.Description,
                keyboard: Keyboard.Default);
            
            await _webSocket.SendUpdateWithdrawalAsync(withdrawal.Id, amount, categoryCode, description ?? "", currency, date);
        }
    }
    
    private async void OnPortfolioDeleteWithdrawalClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is Withdrawal withdrawal)
        {
            var confirm = await DisplayAlert(
                "Delete Withdrawal",
                "Are you sure you want to delete this withdrawal?",
                "Delete",
                "Cancel");
            
            if (confirm)
            {
                await _webSocket.SendDeleteWithdrawalAsync(withdrawal.Id);
            }
        }
    }
    
    private async Task<DateTime> ShowDatePickerDialog(DateTime initialDate)
    {
        var tcs = new TaskCompletionSource<DateTime>();
        var selectedDate = initialDate;

        var datePicker = new DatePicker
        {
            Date = initialDate,
            MinimumDate = new DateTime(2020, 1, 1),
            MaximumDate = DateTime.Now
        };

        datePicker.DateSelected += (s, e) =>
        {
            selectedDate = e.NewDate;
        };

        var okButton = new Button { Text = "OK", BackgroundColor = Color.FromArgb("#1e3a5f"), TextColor = Colors.White };
        okButton.Clicked += (s, e) =>
        {
            tcs.SetResult(selectedDate);
            Shell.Current.Navigation.PopModalAsync();
        };

        var cancelButton = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#5f1e3a"), TextColor = Colors.White };
        cancelButton.Clicked += (s, e) =>
        {
            tcs.SetResult(initialDate);
            Shell.Current.Navigation.PopModalAsync();
        };

        var dialog = new ContentPage
        {
            Title = "Select Date",
            BackgroundColor = Color.FromArgb("#0f0f1a"),
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 20,
                Children =
                {
                    new Label { Text = "Select Date", FontSize = 18, TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
                    datePicker,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        await Shell.Current.Navigation.PushModalAsync(dialog);
        return await tcs.Task;
    }

    private async Task<DateTime> ShowDateTimePickerDialog(DateTime initialDateTime)
    {
        var now = DateTime.Now;
        var result = await DisplayActionSheet(
            "Select Date & Time",
            "Cancel",
            null,
            $"Now: {now:MM/dd HH:mm}",
            $"5 min ago: {now.AddMinutes(-5):MM/dd HH:mm}",
            $"15 min ago: {now.AddMinutes(-15):MM/dd HH:mm}",
            $"30 min ago: {now.AddMinutes(-30):MM/dd HH:mm}",
            $"1 hour ago: {now.AddHours(-1):MM/dd HH:mm}",
            $"Custom Date...");

        if (result == "Cancel" || string.IsNullOrEmpty(result))
            return initialDateTime;

        if (result == "Custom Date...")
        {
            // For custom date, use a simple date input
            var dateStr = await DisplayPromptAsync("Custom Date", "Enter date (MM/dd/yyyy):", initialValue: now.ToString("MM/dd/yyyy"));
            if (string.IsNullOrEmpty(dateStr) || !DateTime.TryParse(dateStr, out var customDate))
                return initialDateTime;

            var timeStr = await DisplayPromptAsync("Custom Time", "Enter time (HH:mm):", initialValue: now.ToString("HH:mm"));
            if (string.IsNullOrEmpty(timeStr) || !TimeSpan.TryParse(timeStr, out var customTime))
                return initialDateTime;

            return customDate.Date + customTime;
        }

        // Parse the selected time from the action sheet
        var timePart = result.Split(':')[1]; // Get HH:mm part
        if (TimeSpan.TryParse(timePart.Trim(), out var selectedTime))
        {
            var selectedDate = now.Date; // Use today's date
            return selectedDate + selectedTime;
        }

        return now;
    }
}
