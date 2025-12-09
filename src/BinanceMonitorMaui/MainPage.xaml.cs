using BinanceMonitorMaui.Models;
using BinanceMonitorMaui.Services;
using System.Collections.ObjectModel;

namespace BinanceMonitorMaui;

public partial class MainPage : ContentPage
{
    private readonly WebSocketService _webSocket;
    private readonly ObservableCollection<Position> _positions = new();
    private const string UrlKey = "websocket_url";
    private const string DefaultUrl = "ws://192.168.1.100:8765/";

    public MainPage()
    {
        InitializeComponent();
        
        _webSocket = new WebSocketService();
        PositionsCollection.ItemsSource = _positions;

        _webSocket.OnConnectionStatusChanged += (connected, message) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (connected)
                {
                    StatusLabel.Text = "Connected";
                    StatusLabel.TextColor = Color.FromArgb("#4ade80");
                }
                else
                {
                    StatusLabel.Text = "Disconnected";
                    StatusLabel.TextColor = Color.FromArgb("#e94560");
                    PositionCountLabel.Text = "";
                    _positions.Clear();
                    
                    // Auto-reconnect after 5 seconds
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000);
                        await ConnectToSavedUrl();
                    });
                }
            });
        };

        _webSocket.OnPositionsUpdated += (positions) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _positions.Clear();
                foreach (var pos in positions)
                {
                    _positions.Add(pos);
                }
                PositionCountLabel.Text = $"({positions.Count} positions)";
            });
        };

        _webSocket.OnAlert += (title, message, isProfit) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert(title, message, "OK");
            });
        };

        // Auto-connect on startup
        _ = ConnectToSavedUrl();
    }

    private async Task ConnectToSavedUrl()
    {
        var url = Preferences.Get(UrlKey, DefaultUrl);
        if (!string.IsNullOrEmpty(url))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusLabel.Text = "Connecting...";
                StatusLabel.TextColor = Color.FromArgb("#f59e0b");
            });
            await _webSocket.ConnectAsync(url);
        }
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        var currentUrl = Preferences.Get(UrlKey, DefaultUrl);
        
        var action = await DisplayActionSheet(
            "Settings", 
            "Cancel", 
            null,
            $"Server: {currentUrl}",
            "Change Server",
            "Reconnect",
            "Disconnect");

        switch (action)
        {
            case "Change Server":
                var newUrl = await DisplayPromptAsync(
                    "Server Address",
                    "Enter WebSocket URL:",
                    initialValue: currentUrl,
                    keyboard: Keyboard.Url);
                    
                if (!string.IsNullOrEmpty(newUrl))
                {
                    Preferences.Set(UrlKey, newUrl);
                    await _webSocket.DisconnectAsync();
                    await Task.Delay(500);
                    await ConnectToSavedUrl();
                }
                break;
                
            case "Reconnect":
                await _webSocket.DisconnectAsync();
                await Task.Delay(500);
                await ConnectToSavedUrl();
                break;
                
            case "Disconnect":
                await _webSocket.DisconnectAsync();
                break;
        }
    }
}
