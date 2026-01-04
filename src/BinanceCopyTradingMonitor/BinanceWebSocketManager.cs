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
        public event Action? OnPortfolioAnalysisRequested;
        public event Func<string, string, string, Task<bool>>? OnClickTPSLRequested; // trader, symbol, size
        public event Func<string, string, string, Task<bool>>? OnClosePositionRequested; // trader, symbol, size
        public event Func<string, Task<bool>>? OnCloseModalRequested; // trader
        public event Func<string, (bool Success, decimal AvgPnL, decimal AvgPnLPercent, int DataPoints, string Message)>? OnGetAvgPnLRequested; // uniqueKey
        public event Func<PortfolioData>? OnGetPortfolioRequested;
        public event Action<decimal, DateTime>? OnUpdateInitialValueRequested;
        public event Action<decimal, string, DateTime>? OnAddGrowthUpdateRequested;
        public event Action<decimal>? OnUpdateCurrentValueRequested;
        public event Action<decimal, string, string, string>? OnAddWithdrawalRequested;
        public event Func<string, decimal?, string?, string?, string?, bool>? OnUpdateWithdrawalRequested;
        public event Func<string, bool>? OnDeleteWithdrawalRequested;
        public event Func<Task<string>>? OnScrapeGrowthRequested;

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
                        
                    case "portfolio_analysis":
                        Log("Portfolio analysis requested");
                        await SendToClientAsync(webSocket, new { type = "portfolio_analysis_started" }, cancellationToken);
                        OnPortfolioAnalysisRequested?.Invoke();
                        break;
                        
                    case "click_tpsl":
                        var tpslTrader = (string?)json?.trader ?? "";
                        var tpslSymbol = (string?)json?.symbol ?? "";
                        var tpslSize = (string?)json?.size ?? "";
                        Log($"TP/SL click request for {tpslTrader} - {tpslSymbol} (size: {tpslSize})");
                        
                        if (OnClickTPSLRequested != null)
                        {
                            var success = await OnClickTPSLRequested.Invoke(tpslTrader, tpslSymbol, tpslSize);
                            await SendToClientAsync(webSocket, new 
                            { 
                                type = "tpsl_click_result", 
                                trader = tpslTrader,
                                symbol = tpslSymbol,
                                success = success,
                                message = success ? "TP/SL dialog opened" : "Failed to find TP/SL button"
                            }, cancellationToken);
                        }
                        break;
                        
                    case "close_position":
                        var closeTrader = (string?)json?.trader ?? "";
                        var closeSymbol = (string?)json?.symbol ?? "";
                        var closeSize = (string?)json?.size ?? "";
                        Log($"Close Position request for {closeTrader} - {closeSymbol} (size: {closeSize})");
                        
                        if (OnClosePositionRequested != null)
                        {
                            var posSuccess = await OnClosePositionRequested.Invoke(closeTrader, closeSymbol, closeSize);
                            await SendToClientAsync(webSocket, new 
                            { 
                                type = "close_position_result", 
                                trader = closeTrader,
                                symbol = closeSymbol,
                                success = posSuccess,
                                message = posSuccess ? "Close Position dialog opened" : "Failed to click Close Position"
                            }, cancellationToken);
                        }
                        break;
                        
                    case "close_modal":
                        var modalTrader = (string?)json?.trader ?? "";
                        Log($"Close Modal request for {modalTrader}");
                        
                        if (OnCloseModalRequested != null)
                        {
                            var closeSuccess = await OnCloseModalRequested.Invoke(modalTrader);
                            await SendToClientAsync(webSocket, new 
                            { 
                                type = "close_modal_result", 
                                trader = modalTrader,
                                success = closeSuccess,
                                message = closeSuccess ? "Modal closed" : "Failed to close modal"
                            }, cancellationToken);
                        }
                        break;
                        
                    case "get_avg_pnl":
                        var uniqueKey = (string?)json?.uniqueKey ?? "";
                        Log($"Avg PnL request for {uniqueKey}");
                        
                        if (OnGetAvgPnLRequested != null)
                        {
                            var pnlResult = OnGetAvgPnLRequested.Invoke(uniqueKey);
                            await SendToClientAsync(webSocket, new 
                            { 
                                type = "avg_pnl_result", 
                                success = pnlResult.Success,
                                uniqueKey = uniqueKey,
                                avgPnL = pnlResult.AvgPnL,
                                avgPnLPercent = pnlResult.AvgPnLPercent,
                                dataPoints = pnlResult.DataPoints,
                                message = pnlResult.Message
                            }, cancellationToken);
                        }
                        break;
                        
                    case "get_portfolio":
                        Log("Portfolio data requested");
                        try
                        {
                            if (OnGetPortfolioRequested != null)
                            {
                                var portfolio = OnGetPortfolioRequested.Invoke();
                                Log($"Sending portfolio data: Initial={portfolio.InitialValue}, Current={portfolio.CurrentValue}, Updates={portfolio.GrowthUpdates.Count}, Withdrawals={portfolio.Withdrawals.Count}");
                                await SendToClientAsync(webSocket, new
                                {
                                    type = "portfolio_data",
                                    initialValue = portfolio.InitialValue,
                                    initialDate = portfolio.InitialDate.ToString("o"),
                                    currentValue = portfolio.CurrentValue,
                                    growthUpdates = portfolio.GrowthUpdates,
                                    withdrawals = portfolio.Withdrawals
                                }, cancellationToken);
                            }
                            else
                            {
                                Log("OnGetPortfolioRequested handler not set!");
                                await SendToClientAsync(webSocket, new
                                {
                                    type = "portfolio_data",
                                    initialValue = 0m,
                                    initialDate = DateTime.Now.ToString("o"),
                                    currentValue = 0m,
                                    growthUpdates = new List<object>(),
                                    withdrawals = new List<object>()
                                }, cancellationToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error handling get_portfolio: {ex.Message}");
                            await SendToClientAsync(webSocket, new
                            {
                                type = "portfolio_data",
                                initialValue = 0m,
                                initialDate = DateTime.Now.ToString("o"),
                                currentValue = 0m,
                                growthUpdates = new List<object>(),
                                withdrawals = new List<object>()
                            }, cancellationToken);
                        }
                        break;
                        
                    case "update_initial_value":
                        var initValue = (decimal?)json?.value ?? 0;
                        var initDateStr = (string?)json?.date ?? DateTime.Now.ToString("o");
                        DateTime.TryParse(initDateStr, out var initDate);
                        Log($"Update initial value: {initValue} USDT (date: {initDate:yyyy-MM-dd})");
                        OnUpdateInitialValueRequested?.Invoke(initValue, initDate);
                        await SendToClientAsync(webSocket, new
                        {
                            type = "portfolio_update_result",
                            success = true,
                            message = "Initial value updated"
                        }, cancellationToken);
                        break;
                        
                    case "add_growth_update":
                        var growthValue = (decimal?)json?.value ?? 0;
                        var growthNotes = (string?)json?.notes ?? "";
                        var growthDateStr = (string?)json?.date ?? DateTime.Now.ToString("o");
                        DateTime.TryParse(growthDateStr, out var growthDate);
                        Log($"Add growth update: {growthValue} USDT (date: {growthDate:yyyy-MM-dd HH:mm})");
                        OnAddGrowthUpdateRequested?.Invoke(growthValue, growthNotes, growthDate);
                        await SendToClientAsync(webSocket, new
                        {
                            type = "portfolio_update_result",
                            success = true,
                            message = "Growth update added"
                        }, cancellationToken);
                        break;
                        
                    case "update_current_value":
                        var currentValue = (decimal?)json?.value ?? 0;
                        Log($"Update current value: {currentValue} USDT");
                        OnUpdateCurrentValueRequested?.Invoke(currentValue);
                        await SendToClientAsync(webSocket, new
                        {
                            type = "portfolio_update_result",
                            success = true,
                            message = "Current value updated"
                        }, cancellationToken);
                        break;
                        
                    case "add_withdrawal":
                        var withdrawalAmount = (decimal?)json?.amount ?? 0;
                        var withdrawalCategory = (string?)json?.category ?? "";
                        var withdrawalDescription = (string?)json?.description ?? "";
                        var withdrawalCurrency = (string?)json?.currency ?? "USDT";
                        Log($"Add withdrawal: {withdrawalAmount} {withdrawalCurrency} ({withdrawalCategory})");
                        OnAddWithdrawalRequested?.Invoke(withdrawalAmount, withdrawalCategory, withdrawalDescription, withdrawalCurrency);
                        await SendToClientAsync(webSocket, new
                        {
                            type = "portfolio_update_result",
                            success = true,
                            message = "Withdrawal added"
                        }, cancellationToken);
                        break;
                        
                    case "update_withdrawal":
                        var updateId = (string?)json?.id ?? "";
                        var updateAmount = (decimal?)json?.amount;
                        var updateCategory = (string?)json?.category;
                        var updateDescription = (string?)json?.description;
                        var updateCurrency = (string?)json?.currency;
                        Log($"Update withdrawal: {updateId}");
                        if (OnUpdateWithdrawalRequested != null)
                        {
                            var updateSuccess = OnUpdateWithdrawalRequested.Invoke(updateId, updateAmount, updateCategory, updateDescription, updateCurrency);
                            await SendToClientAsync(webSocket, new
                            {
                                type = "portfolio_update_result",
                                success = updateSuccess,
                                message = updateSuccess ? "Withdrawal updated" : "Withdrawal not found"
                            }, cancellationToken);
                        }
                        break;
                        
                    case "delete_withdrawal":
                        var deleteId = (string?)json?.id ?? "";
                        Log($"Delete withdrawal: {deleteId}");
                        if (OnDeleteWithdrawalRequested != null)
                        {
                            var deleteSuccess = OnDeleteWithdrawalRequested.Invoke(deleteId);
                            await SendToClientAsync(webSocket, new
                            {
                                type = "portfolio_update_result",
                                success = deleteSuccess,
                                message = deleteSuccess ? "Withdrawal deleted" : "Withdrawal not found"
                            }, cancellationToken);
                        }
                        break;

                    case "scrape_growth":
                        Log("Growth scraping requested");
                        if (OnScrapeGrowthRequested != null)
                        {
                            var scrapedGrowthValue = await OnScrapeGrowthRequested.Invoke();
                            await SendToClientAsync(webSocket, new
                            {
                                type = "growth_scraped",
                                value = scrapedGrowthValue,
                                timestamp = DateTime.UtcNow.ToString("o")
                            }, cancellationToken);
                        }
                        else
                        {
                            await SendToClientAsync(webSocket, new
                            {
                                type = "growth_scraped",
                                value = "NOT_SUPPORTED",
                                timestamp = DateTime.UtcNow.ToString("o")
                            }, cancellationToken);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Error handling client message: {ex.Message}\n{ex.StackTrace}");
            }
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
