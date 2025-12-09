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
        public event Action<bool, string>? OnConnectionStatusChanged;
        public event Action<string, string, bool>? OnAlert;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        public async Task ConnectAsync(string url)
        {
            try
            {
                _cts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                var uri = new Uri(url);
                await _webSocket.ConnectAsync(uri, _cts.Token);

                OnConnectionStatusChanged?.Invoke(true, "Connected");

                _ = Task.Run(() => ReceiveLoop());
            }
            catch (Exception ex)
            {
                OnConnectionStatusChanged?.Invoke(false, $"Failed: {ex.Message}");
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
                        break;
                        
                    case "alert":
                        OnAlert?.Invoke(
                            message.title ?? "Alert",
                            message.message ?? "",
                            message.isProfit ?? false);
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
}
