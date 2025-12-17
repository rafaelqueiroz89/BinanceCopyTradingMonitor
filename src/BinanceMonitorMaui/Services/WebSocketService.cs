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
        public event Action<AnalysisResult>? OnAnalysisResult;
        public event Action<PortfolioAnalysisResult>? OnPortfolioAnalysisResult;
        public event Action<string, string, bool, string>? OnTPSLClickResult;
        public event Action<string, bool, string>? OnCloseModalResult;
        public event Action<string, string, bool, string>? OnClosePositionResult;
        public event Action<AvgPnLResult>? OnAvgPnLResult;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        public async Task ConnectAsync(string url, string? token = null)
        {
            try
            {
                _cts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                var uri = new Uri(url);
                await _webSocket.ConnectAsync(uri, _cts.Token);

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
                var authMessage = JsonSerializer.Serialize(new { type = "auth", token = token });
                var buffer = Encoding.UTF8.GetBytes(authMessage);
                await _webSocket!.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts!.Token);

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
                        
                    case "analysis_result":
                        var result = new AnalysisResult
                        {
                            Symbol = message.symbol ?? "",
                            Recommendation = message.recommendation ?? "",
                            Confidence = (int)(message.confidence ?? 0),
                            Summary = message.summary ?? "",
                            Trader = message.trader ?? "",
                            CurrentPnl = message.currentPnl ?? "",
                            CurrentPnlPercent = message.currentPnlPercent ?? ""
                        };
                        System.Diagnostics.Debug.WriteLine($"[WS] Got analysis: {result.Symbol} = {result.Recommendation}");
                        OnAnalysisResult?.Invoke(result);
                        break;
                        
                    case "portfolio_analysis_result":
                        var portfolioResult = new PortfolioAnalysisResult
                        {
                            Analysis = message.analysis ?? "",
                            Summary = message.summary ?? "",
                            TotalPositions = message.totalPositions ?? 0,
                            TotalPnL = message.totalPnL ?? 0,
                            Insights = (message.insights ?? new List<PositionInsightMsg>())
                                .Select(i => new PositionInsight
                                {
                                    Symbol = i.symbol ?? "",
                                    Trader = i.trader ?? "",
                                    Recommendation = i.recommendation ?? "",
                                    Insight = i.insight ?? "",
                                    MarketData = i.marketData ?? ""
                                }).ToList()
                        };
                        System.Diagnostics.Debug.WriteLine($"[WS] Got portfolio analysis: {portfolioResult.TotalPositions} positions");
                        OnPortfolioAnalysisResult?.Invoke(portfolioResult);
                        break;
                        
                    case "tpsl_click_result":
                        var tpslTrader = message.trader ?? "";
                        var tpslSymbol = message.symbol ?? "";
                        var tpslSuccess = message.success ?? false;
                        var tpslMessage = message.message ?? "";
                        System.Diagnostics.Debug.WriteLine($"[WS] TP/SL click result: {tpslSymbol} = {tpslSuccess}");
                        OnTPSLClickResult?.Invoke(tpslTrader, tpslSymbol, tpslSuccess, tpslMessage);
                        break;
                        
                    case "close_modal_result":
                        var modalTrader = message.trader ?? "";
                        var modalSuccess = message.success ?? false;
                        var modalMessage = message.message ?? "";
                        System.Diagnostics.Debug.WriteLine($"[WS] Close Modal result: {modalSuccess}");
                        OnCloseModalResult?.Invoke(modalTrader, modalSuccess, modalMessage);
                        break;
                        
                    case "close_position_result":
                        var closePosTrader = message.trader ?? "";
                        var closePosSymbol = message.symbol ?? "";
                        var closePosSuccess = message.success ?? false;
                        var closePosMessage = message.message ?? "";
                        System.Diagnostics.Debug.WriteLine($"[WS] Close Position result: {closePosSymbol} = {closePosSuccess}");
                        OnClosePositionResult?.Invoke(closePosTrader, closePosSymbol, closePosSuccess, closePosMessage);
                        break;
                        
                    case "avg_pnl_result":
                        var avgPnLInfo = new AvgPnLResult
                        {
                            Success = message.success ?? false,
                            UniqueKey = message.uniqueKey ?? "",
                            AvgPnL = message.avgPnL ?? 0,
                            AvgPnLPercent = message.avgPnLPercent ?? 0,
                            DataPoints = message.dataPoints ?? 0,
                            Message = message.message ?? ""
                        };
                        System.Diagnostics.Debug.WriteLine($"[WS] Avg PnL result: {avgPnLInfo.UniqueKey} = {avgPnLInfo.AvgPnL}");
                        OnAvgPnLResult?.Invoke(avgPnLInfo);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WS] ProcessMessage error: {ex.Message}");
            }
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

        public async Task SendRefreshAsync()
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "refresh" });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }
        
        public async Task SendRestartAsync()
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "restart" });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }
        
        public async Task SendAnalyzeAsync(string symbol)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "analyze", symbol = symbol });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }
        
        public async Task SendPortfolioAnalysisAsync()
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "portfolio_analysis" });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }
        
        public async Task SendClickTPSLAsync(string trader, string symbol, string size)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "click_tpsl", trader, symbol, size });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }
        
        public async Task SendCloseModalAsync(string trader)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "close_modal", trader });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }
        
        public async Task SendClosePositionAsync(string trader, string symbol, string size)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "close_position", trader, symbol, size });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }
        
        public async Task SendGetAvgPnLAsync(string uniqueKey)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open) return;
                
                var message = System.Text.Json.JsonSerializer.Serialize(new { type = "get_avg_pnl", uniqueKey });
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
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
