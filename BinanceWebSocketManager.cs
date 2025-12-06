using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BinanceCopyTradingMonitor
{
    public class BinanceWebSocketManager : IDisposable
    {
        private HttpListener? _httpListener;
        private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning;
        private readonly int _port;
        private List<ScrapedPosition> _latestPositions = new();
        private readonly object _positionsLock = new();

        public event Action<string>? OnLog;
        public event Action<string>? OnError;
        public event Action<int>? OnClientCountChanged;

        public int ConnectedClients => _clients.Count;
        public int Port => _port;

        public BinanceWebSocketManager(int port = 8765)
        {
            _port = port;
        }

        public async Task<bool> StartAsync()
        {
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://+:{_port}/");
                
                try
                {
                    _httpListener.Start();
                }
                catch (HttpListenerException)
                {
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://localhost:{_port}/");
                    _httpListener.Start();
                    Log($"⚠️ WebSocket server running on localhost only (port {_port})");
                }

                _isRunning = true;
                Log($"🚀 WebSocket server started on port {_port}");
                Log($"📱 Android clients can connect to: ws://<YOUR_IP>:{_port}/");

                _ = Task.Run(() => AcceptClientsAsync(_cancellationTokenSource.Token));

                return true;
            }
            catch (Exception ex)
            {
                Error($"Failed to start WebSocket server: {ex.Message}");
                return false;
            }
        }

        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener!.GetContextAsync();
                    
                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = Task.Run(() => HandleClientAsync(context, cancellationToken));
                    }
                    else
                    {
                        var response = context.Response;
                        var statusJson = JsonConvert.SerializeObject(new
                        {
                            status = "running",
                            clients = _clients.Count,
                            positions = _latestPositions.Count,
                            timestamp = DateTime.UtcNow
                        });
                        
                        var buffer = Encoding.UTF8.GetBytes(statusJson);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        response.Close();
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Error($"Error accepting client: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocket? webSocket = null;
            var clientId = Guid.NewGuid().ToString();

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;
                
                var clientIp = context.Request.RemoteEndPoint?.ToString() ?? "unknown";
                _clients[clientId] = webSocket;
                
                Log($"✅ Client connected: {clientIp} (Total: {_clients.Count})");
                OnClientCountChanged?.Invoke(_clients.Count);

                await SendCurrentPositionsToClientAsync(webSocket, cancellationToken);

                var buffer = new byte[4096];
                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested close", cancellationToken);
                        break;
                    }
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await HandleClientMessageAsync(webSocket, message, cancellationToken);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Log($"WebSocket error: {ex.Message}");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Error($"Client error: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                webSocket?.Dispose();
                Log($"❌ Client disconnected (Total: {_clients.Count})");
                OnClientCountChanged?.Invoke(_clients.Count);
            }
        }

        private async Task HandleClientMessageAsync(WebSocket webSocket, string message, CancellationToken cancellationToken)
        {
            try
            {
                var json = JsonConvert.DeserializeObject<dynamic>(message);
                var type = (string?)json?.type;

                switch (type)
                {
                    case "ping":
                        await SendToClientAsync(webSocket, new { type = "pong", timestamp = DateTime.UtcNow }, cancellationToken);
                        break;
                        
                    case "get_positions":
                        await SendCurrentPositionsToClientAsync(webSocket, cancellationToken);
                        break;
                        
                    default:
                        Log($"📨 Received from client: {message}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Error($"Error handling client message: {ex.Message}");
            }
        }

        private async Task SendCurrentPositionsToClientAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            List<ScrapedPosition> positions;
            lock (_positionsLock)
            {
                positions = new List<ScrapedPosition>(_latestPositions);
            }

            var message = new
            {
                type = "positions",
                data = positions,
                count = positions.Count,
                timestamp = DateTime.UtcNow
            };

            await SendToClientAsync(webSocket, message, cancellationToken);
        }

        private async Task SendToClientAsync(WebSocket webSocket, object message, CancellationToken cancellationToken)
        {
            try
            {
                if (webSocket.State != WebSocketState.Open) return;

                var json = JsonConvert.SerializeObject(message);
                var buffer = Encoding.UTF8.GetBytes(json);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Error($"Error sending to client: {ex.Message}");
            }
        }

        public async Task BroadcastPositionsAsync(List<ScrapedPosition> positions)
        {
            lock (_positionsLock)
            {
                _latestPositions = new List<ScrapedPosition>(positions);
            }

            var message = new
            {
                type = "positions",
                data = positions,
                count = positions.Count,
                timestamp = DateTime.UtcNow
            };

            var json = JsonConvert.SerializeObject(message);
            var buffer = Encoding.UTF8.GetBytes(json);

            var deadClients = new List<string>();

            foreach (var kvp in _clients)
            {
                try
                {
                    if (kvp.Value.State == WebSocketState.Open)
                    {
                        await kvp.Value.SendAsync(
                            new ArraySegment<byte>(buffer),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);
                    }
                    else
                    {
                        deadClients.Add(kvp.Key);
                    }
                }
                catch
                {
                    deadClients.Add(kvp.Key);
                }
            }

            foreach (var clientId in deadClients)
            {
                _clients.TryRemove(clientId, out var ws);
                ws?.Dispose();
            }

            if (deadClients.Count > 0)
            {
                OnClientCountChanged?.Invoke(_clients.Count);
            }

            if (_clients.Count > 0)
            {
                Log($"📤 Broadcast to {_clients.Count} clients: {positions.Count} positions");
            }
        }

        public async Task BroadcastMessageAsync(object message)
        {
            var json = JsonConvert.SerializeObject(message);
            var buffer = Encoding.UTF8.GetBytes(json);

            foreach (var kvp in _clients)
            {
                try
                {
                    if (kvp.Value.State == WebSocketState.Open)
                    {
                        await kvp.Value.SendAsync(
                            new ArraySegment<byte>(buffer),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);
                    }
                }
                catch { }
            }
        }

        public async Task BroadcastAlertAsync(string title, string message, bool isProfit)
        {
            var alert = new
            {
                type = "alert",
                title = title,
                message = message,
                isProfit = isProfit,
                timestamp = DateTime.UtcNow
            };

            await BroadcastMessageAsync(alert);
        }

        public void Stop()
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            foreach (var kvp in _clients)
            {
                try
                {
                    kvp.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None).Wait(1000);
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _clients.Clear();

            _httpListener?.Stop();
            _httpListener?.Close();

            Log("🛑 WebSocket server stopped");
        }

        private void Log(string message)
        {
            Console.WriteLine($"[WS] {message}");
            OnLog?.Invoke(message);
        }

        private void Error(string message)
        {
            Console.WriteLine($"[WS ERROR] {message}");
            OnError?.Invoke(message);
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}
