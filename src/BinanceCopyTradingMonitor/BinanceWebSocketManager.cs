using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        private readonly ConcurrentDictionary<string, (WebSocket Socket, bool IsAuthenticated)> _clients = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning;
        private readonly int _port;
        private readonly string? _authToken;
        private List<ScrapedPosition> _latestPositions = new();
        private readonly object _positionsLock = new();

        public event Action<string>? OnLog;
        public event Action<string>? OnError;
        public event Action<int>? OnClientCountChanged;
        public event Action? OnRefreshRequested;
        public event Action? OnRestartRequested;
        public event Action<string>? OnAnalyzeRequested;

        public int ConnectedClients => _clients.Count(c => c.Value.IsAuthenticated);
        public int Port => _port;
        public bool RequiresAuth => !string.IsNullOrEmpty(_authToken);

        public BinanceWebSocketManager(int port = 8765, string? authToken = null)
        {
            _port = port;
            _authToken = authToken;
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
                    Log($"WARNING: WebSocket server running on localhost only (port {_port})");
                }

                _isRunning = true;
                var authStatus = RequiresAuth ? " (token auth enabled)" : "";
                Log($"WebSocket server started on port {_port}{authStatus}");

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
                            clients = ConnectedClients,
                            positions = _latestPositions.Count,
                            requiresAuth = RequiresAuth,
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
            var clientIp = context.Request.RemoteEndPoint?.ToString() ?? "unknown";

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;
                
                bool isAuthenticated = !RequiresAuth;
                _clients[clientId] = (webSocket, isAuthenticated);
                
                Log($"Client connected: {clientIp}");

                if (RequiresAuth)
                {
                    var authSuccess = await WaitForAuthenticationAsync(webSocket, clientId, cancellationToken);
                    
                    if (!authSuccess)
                    {
                        Log($"Client {clientIp} failed authentication");
                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication failed", CancellationToken.None);
                        return;
                    }
                    
                    Log($"Client {clientIp} authenticated (Total: {ConnectedClients})");
                }
                
                OnClientCountChanged?.Invoke(ConnectedClients);
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
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            finally
            {
                _clients.TryRemove(clientId, out _);
                webSocket?.Dispose();
                OnClientCountChanged?.Invoke(ConnectedClients);
            }
        }

        private async Task<bool> WaitForAuthenticationAsync(WebSocket webSocket, string clientId, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                var buffer = new byte[1024];
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), linkedCts.Token);
                
                if (result.MessageType != WebSocketMessageType.Text)
                    return false;
                
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var json = JsonConvert.DeserializeObject<dynamic>(message);
                
                var type = (string?)json?.type;
                var token = (string?)json?.token;
                
                if (type == "auth" && token == _authToken)
                {
                    _clients[clientId] = (webSocket, true);
                    
                    await SendToClientAsync(webSocket, new { type = "auth_success" }, cancellationToken);
                    return true;
                }
                
                await SendToClientAsync(webSocket, new { type = "auth_failed", reason = "Invalid token" }, cancellationToken);
                return false;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await SendToClientAsync(webSocket, new { type = "auth_failed", reason = "Timeout" }, CancellationToken.None);
                }
                catch { }
                return false;
            }
            catch
            {
                return false;
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
                        
                    case "refresh":
                        Log("Refresh command received");
                        OnRefreshRequested?.Invoke();
                        await SendToClientAsync(webSocket, new { type = "refresh_started", timestamp = DateTime.UtcNow }, cancellationToken);
                        break;
                        
                    case "restart":
                        Log("Restart command received");
                        await SendToClientAsync(webSocket, new { type = "restart_started", timestamp = DateTime.UtcNow }, cancellationToken);
                        OnRestartRequested?.Invoke();
                        break;
                        
                    case "analyze":
                        var symbol = (string?)json?.symbol ?? "";
                        Log($"Analyze request for {symbol}");
                        await SendToClientAsync(webSocket, new { type = "analysis_started", symbol = symbol }, cancellationToken);
                        OnAnalyzeRequested?.Invoke(symbol);
                        break;
                }
            }
            catch { }
        }

        private async Task SendCurrentPositionsToClientAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            List<ScrapedPosition> positions;
            lock (_positionsLock)
            {
                positions = new List<ScrapedPosition>(_latestPositions);
            }

            var totalPnL = positions.Sum(p => p.PnL);
            var totalPnLPercentage = positions.Count > 0 ? positions.Average(p => p.PnLPercentage) : 0;

            var message = new
            {
                type = "positions",
                data = positions,
                count = positions.Count,
                totalPnL = Math.Round(totalPnL, 2),
                totalPnLPercentage = Math.Round(totalPnLPercentage, 2),
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
            catch { }
        }

        public async Task BroadcastPositionsAsync(List<ScrapedPosition> positions)
        {
            lock (_positionsLock)
            {
                _latestPositions = new List<ScrapedPosition>(positions);
            }

            var totalPnL = positions.Sum(p => p.PnL);
            var totalPnLPercentage = positions.Count > 0 ? positions.Average(p => p.PnLPercentage) : 0;

            var message = new
            {
                type = "positions",
                data = positions,
                count = positions.Count,
                totalPnL = Math.Round(totalPnL, 2),
                totalPnLPercentage = Math.Round(totalPnLPercentage, 2),
                timestamp = DateTime.UtcNow
            };

            var json = JsonConvert.SerializeObject(message);
            var buffer = Encoding.UTF8.GetBytes(json);

            var deadClients = new List<string>();

            foreach (var kvp in _clients)
            {
                if (!kvp.Value.IsAuthenticated) continue;
                
                try
                {
                    if (kvp.Value.Socket.State == WebSocketState.Open)
                    {
                        await kvp.Value.Socket.SendAsync(
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
                _clients.TryRemove(clientId, out var client);
                client.Socket?.Dispose();
            }

            if (deadClients.Count > 0)
            {
                OnClientCountChanged?.Invoke(ConnectedClients);
            }
        }

        public async Task BroadcastMessageAsync(object message)
        {
            var json = JsonConvert.SerializeObject(message);
            var buffer = Encoding.UTF8.GetBytes(json);
            var sentCount = 0;

            foreach (var kvp in _clients)
            {
                if (!kvp.Value.IsAuthenticated) continue;
                
                try
                {
                    if (kvp.Value.Socket.State == WebSocketState.Open)
                    {
                        await kvp.Value.Socket.SendAsync(
                            new ArraySegment<byte>(buffer),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);
                        sentCount++;
                    }
                }
                catch { }
            }
            
            if (json.Contains("analysis_result"))
                Log($"Broadcast analysis_result to {sentCount} clients");
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
            try
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel();

                foreach (var kvp in _clients)
                {
                    try
                    {
                        if (kvp.Value.Socket.State == WebSocketState.Open)
                        {
                            kvp.Value.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None).Wait(500);
                        }
                        kvp.Value.Socket.Dispose();
                    }
                    catch { }
                }
                _clients.Clear();

                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }
        }

        private void Log(string message)
        {
            if (OnLog != null)
                try { OnLog.Invoke($"[WS] {message}"); } catch { }
            else
                try { Console.WriteLine($"[WS] {message}"); } catch { }
        }

        private void Error(string message)
        {
            if (OnError != null)
                try { OnError.Invoke($"[WS ERROR] {message}"); } catch { }
            else
                try { Console.WriteLine($"[WS ERROR] {message}"); } catch { }
        }

        public void Dispose()
        {
            try { Stop(); } catch { }
            try { _cancellationTokenSource?.Dispose(); } catch { }
        }
    }
}

