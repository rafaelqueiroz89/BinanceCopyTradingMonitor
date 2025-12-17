using BinanceMonitorMaui.Models;
using BinanceMonitorMaui.Services;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;

namespace BinanceMonitorMaui;

public partial class MainPage : ContentPage
{
    private readonly WebSocketService _webSocket;
    private readonly AlertService _alertService;
    private readonly ObservableCollection<TraderGroup> _groupedPositions = new();
    private const string UrlKey = "websocket_url";
    private const string TokenKey = "websocket_token";
    private const string DefaultUrl = "ws://192.168.1.100:8765/";
    private bool _isConnecting;

	public MainPage()
	{
		InitializeComponent();
        
        _webSocket = new WebSocketService();
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
        
        _alertService.OnAlertTriggered += OnCustomAlertTriggered;

        Dispatcher.Dispatch(async () => await ConnectToSavedUrl());
    }
    
    private void OnCustomAlertTriggered(string symbol, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await DisplayAlert($"🔔 Alert: {symbol}", message, "OK");
        });
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

    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        var action = await DisplayActionSheet(
            "Menu",
            "Cancel",
            null,
            "🔄 Refresh Pages",
            "📊 Portfolio Analysis",
            "🔌 Restart Chrome",
            "⚙️ Settings");
            
        switch (action)
        {
            case "🔄 Refresh Pages":
                OnRefreshClicked(sender, e);
                break;
            case "📊 Portfolio Analysis":
                OnPortfolioAnalysisClicked(sender, e);
                break;
            case "🔌 Restart Chrome":
                OnRestartClicked(sender, e);
                break;
            case "⚙️ Settings":
                await ShowSettingsMenu();
                break;
        }
    }
    
    private async Task ShowSettingsMenu()
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

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        if (!_webSocket.IsConnected)
        {
            await DisplayAlert("Not Connected", "Connect to server first", "OK");
            return;
        }
        
        await _webSocket.SendRefreshAsync();
        
        var originalText = StatusLabel.Text;
        var originalColor = StatusLabel.TextColor;
        StatusLabel.Text = "Refreshing...";
        StatusLabel.TextColor = Color.FromArgb("#f59e0b");
        
        await Task.Delay(2000);
        
        if (_webSocket.IsConnected)
        {
            StatusLabel.Text = originalText;
            StatusLabel.TextColor = originalColor;
        }
    }
    
    private async void OnRestartClicked(object? sender, EventArgs e)
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
        
        StatusLabel.Text = "Restarting Chrome...";
        StatusLabel.TextColor = Color.FromArgb("#f59e0b");
        
        await Task.Delay(5000);
        
        if (_webSocket.IsConnected)
        {
            StatusLabel.Text = "Connected";
            StatusLabel.TextColor = Color.FromArgb("#22c55e");
        }
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
    
    private async void OnPortfolioAnalysisClicked(object? sender, EventArgs e)
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
            var toastText = $"{icon} 5m Avg: {result.AvgPnL:+0.00;-0.00} ({result.AvgPnLPercent:+0.00;-0.00}%)";
            
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
}
