using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BinanceMonitorMaui.Models;

namespace BinanceMonitorMaui.Services
{
    public class WebSocketService : IDisposable
    {
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;

        public event Action<List<Position>>? OnPositionsUpdated;
        public event Action<decimal, decimal>? OnTotalsUpdated;
        public event Action<bool, string>? OnConnectionStatusChanged;
        public event Action<string, string, bool>? OnAlert;
        public event Action<QuickGainerAlert>? OnQuickGainer;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        public async Task ConnectAsync(string url, string? token = null)
        {
            try
            {
                _cts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                var uri = new Uri(url);
                await _webSocket.ConnectAsync(uri, _cts.Token);

                // If token provided, authenticate first
                if (!string.IsNullOrEmpty(token))
                {
                    var authSuccess = await AuthenticateAsync(token);
                    if (!authSuccess)
                    {
                        OnConnectionStatusChanged?.Invoke(false, "Auth failed");
                        return;
                    }
                }

                OnConnectionStatusChanged?.Invoke(true, "Connected");
                _ = Task.Run(() => ReceiveLoop());
            }
            catch (Exception ex)
            {
                OnConnectionStatusChanged?.Invoke(false, $"Failed: {ex.Message}");
            }
        }

        private async Task<bool> AuthenticateAsync(string token)
        {
            try
            {
                // Send auth message
                var authMessage = JsonSerializer.Serialize(new { type = "auth", token = token });
                var buffer = Encoding.UTF8.GetBytes(authMessage);
                await _webSocket!.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts!.Token);

                // Wait for response (5 second timeout)
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

                var responseBuffer = new byte[1024];
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(responseBuffer), linkedCts.Token);

                if (result.MessageType != WebSocketMessageType.Text)
                    return false;

                var response = Encoding.UTF8.GetString(responseBuffer, 0, result.Count);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var message = JsonSerializer.Deserialize<AuthResponse>(response, options);

                return message?.type == "auth_success";
            }
            catch
            {
                return false;
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[16384];
            var messageBuilder = new StringBuilder();

            try
            {
                while (_webSocket?.State == WebSocketState.Open && !_cts!.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnConnectionStatusChanged?.Invoke(false, "Disconnected");
                        break;
                    }

                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var message = messageBuilder.ToString();
                        messageBuilder.Clear();
                        ProcessMessage(message);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                OnConnectionStatusChanged?.Invoke(false, "Disconnected");
            }
        }

        private void ProcessMessage(string json)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var message = JsonSerializer.Deserialize<WebSocketMessage>(json, options);

                if (message == null) return;

                switch (message.type)
                {
                    case "positions":
                        if (message.data != null)
                        {
                            OnPositionsUpdated?.Invoke(message.data);
                        }
                        
                        OnTotalsUpdated?.Invoke(
                            message.totalPnL ?? 0,
                            message.totalPnLPercentage ?? 0);
                        break;
                        
                    case "alert":
                        OnAlert?.Invoke(
                            message.title ?? "Alert",
                            message.message ?? "",
                            message.isProfit ?? false);
                        break;
                        
                    case "quick_gainer":
                        var alert = new QuickGainerAlert
                        {
                            AlertType = message.alertType ?? "quick_gainer",
                            Trader = message.trader ?? "",
                            Symbol = message.symbol ?? "",
                            PnL = message.pnl ?? 0,
                            PnLPercentage = message.pnlPercentage ?? 0,
                            Growth = message.growth ?? 0,
                            Message = message.message ?? ""
                        };
                        OnQuickGainer?.Invoke(alert);
                        break;
                }
            }
            catch { }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _cts?.Cancel();

                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "User disconnect", CancellationToken.None);
                }

                OnConnectionStatusChanged?.Invoke(false, "Disconnected");
            }
            catch { }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _webSocket?.Dispose();
        }
    }

    internal class AuthResponse
    {
        public string? type { get; set; }
        public string? reason { get; set; }
    }
}
