using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace BinanceCopyTradingMonitor
{
    public class AppConfig
    {
        public string? WebSocketToken { get; set; }
        public string? OpenAiApiKey { get; set; }
        public decimal QuickGainerThreshold { get; set; } = 10m;
        public decimal ExplosionThreshold { get; set; } = 20m;
        public bool AutoCloseEnabled { get; set; } = true;
        public decimal AutoCloseThreshold { get; set; } = 10m;  // Close at +10%
    }
    
    public class CopyTradingScraperApp : Form
    {
        private NotifyIcon _trayIcon = new NotifyIcon();
        private BinanceScraperManager? _scraper;
        private BinanceWebSocketManager? _webSocketServer;
        private PositionWidget? _positionWidget;
        private CoinAnalysisService? _analysisService;
        private AppConfig? _config;
        private List<ScrapedPosition> _lastPositions = new();
        private ToolStripMenuItem? _toggleWidgetItem;
        private ToolStripMenuItem? _toggleConsoleItem;
        
        // PnL history tracking (key: UniqueKey, value: list of (timestamp, pnl, pnlPercent))
        private Dictionary<string, List<(DateTime Time, decimal PnL, decimal PnLPercent)>> _pnlHistory = new();
        private const int PNL_HISTORY_MAX_MINUTES = 10;
        
        // Auto-close tracking
        private ClosedPositionsStore _closedPositionsStore = new();
        private HashSet<string> _autoCloseInProgress = new();
        
        // Portfolio tracking
        private PortfolioStore _portfolioStore = new();

        private const int WEBSOCKET_PORT = 8765;

        public CopyTradingScraperApp()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Opacity = 0;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(1, 1);
            
            InitializeTray();
            _ = StartAsync();
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);
        
        private void InitializeTray()
        {
            try
            {
                var iconHandle = ExtractIcon(IntPtr.Zero, "shell32.dll", 21);
                if (iconHandle != IntPtr.Zero)
                    _trayIcon.Icon = Icon.FromHandle(iconHandle);
                else
                    _trayIcon.Icon = SystemIcons.Application;
            }
            catch { _trayIcon.Icon = SystemIcons.Application; }
            
            _trayIcon.Text = "Copy Trading Monitor";
            _trayIcon.Visible = true;

            var menu = new ContextMenuStrip();
            
            _toggleWidgetItem = new ToolStripMenuItem("Hide Positions", null, (s, e) => ToggleWidget());
            _toggleConsoleItem = new ToolStripMenuItem("Hide Console", null, (s, e) => ToggleConsole());
            
            menu.Items.Add(_toggleWidgetItem);
            menu.Items.Add(_toggleConsoleItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("📊 Closed Positions", null, (s, e) => ShowClosedPositionsSummary());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Refresh Pages", null, (s, e) => { _scraper?.RequestRefresh(); });
            menu.Items.Add("🔄 Restart Chrome", null, (s, e) => RestartScraper());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => { Application.Exit(); });
            
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => ToggleWidget();
        }
        
        private async void RestartScraper()
        {
            Console.WriteLine("[RESTART] Restarting scraper (Chrome only)...");
            try { _scraper?.Stop(); } catch { }
            
            try
            {
                var processes = System.Diagnostics.Process.GetProcesses()
                    .Where(p => p.ProcessName.ToLower().Contains("chrom"))
                    .ToList();
                foreach (var process in processes)
                {
                    try { process.Kill(); } catch { }
                }
                Console.WriteLine($"[RESTART] Killed {processes.Count} Chrome processes");
            }
            catch { }
            
            await Task.Delay(1000);
            await StartScraperAsync();
            Console.WriteLine("[RESTART] Scraper restarted!");
        }
        
        private async Task AnalyzePositionAsync(string symbol)
        {
            if (_analysisService == null || _webSocketServer == null)
            {
                Console.WriteLine("[ANALYZE] Service not initialized");
                return;
            }
            
            var position = _lastPositions.FirstOrDefault(p => 
                p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) ||
                p.Symbol.Contains(symbol, StringComparison.OrdinalIgnoreCase));
            
            if (position == null)
            {
                await _webSocketServer.BroadcastMessageAsync(new
                {
                    type = "analysis_result",
                    symbol = symbol,
                    recommendation = "ERROR",
                    confidence = 0,
                    summary = $"Position '{symbol}' not found"
                });
                return;
            }
            
            Console.WriteLine($"[ANALYZE] Analyzing {position.Symbol}...");
            var result = await _analysisService.AnalyzePositionAsync(position);
            
            await _webSocketServer.BroadcastMessageAsync(new
            {
                type = "analysis_result",
                symbol = result.Symbol,
                recommendation = result.Recommendation,
                confidence = result.Confidence,
                summary = result.Summary,
                trader = position.Trader,
                currentPnl = $"{position.PnL:+0.00;-0.00} {position.PnLCurrency}",
                currentPnlPercent = $"{position.PnLPercentage:+0.00;-0.00}%"
            });
            
            Console.WriteLine($"[ANALYZE] Result: {result.Recommendation} ({result.Confidence}%)");
        }
        
        private async Task AnalyzePortfolioAsync()
        {
            if (_analysisService == null || _webSocketServer == null)
            {
                Console.WriteLine("[PORTFOLIO] Service not initialized");
                return;
            }
            
            if (_lastPositions.Count == 0)
            {
                await _webSocketServer.BroadcastMessageAsync(new
                {
                    type = "portfolio_analysis_result",
                    analysis = "No positions to analyze",
                    summary = "No open positions found",
                    totalPositions = 0,
                    totalPnL = 0m,
                    insights = new List<object>()
                });
                return;
            }
            
            Console.WriteLine($"[PORTFOLIO] Analyzing {_lastPositions.Count} positions...");
            var result = await _analysisService.AnalyzePortfolioAsync(_lastPositions);
            
            Console.WriteLine($"[PORTFOLIO] Broadcasting result...");
            
            var broadcastMsg = new
            {
                type = "portfolio_analysis_result",
                analysis = result.Analysis,
                summary = result.Summary,
                totalPositions = result.TotalPositions,
                totalPnL = result.TotalPnL,
                insights = result.Insights.Select(i => new
                {
                    symbol = i.Symbol,
                    trader = i.Trader,
                    recommendation = i.Recommendation,
                    insight = i.Insight,
                    marketData = i.MarketData
                }).ToList()
            };
            
            await _webSocketServer.BroadcastMessageAsync(broadcastMsg);
            
            Console.WriteLine($"[PORTFOLIO] Broadcast complete! {result.Insights.Count} insights sent");
        }
        
        private void ToggleWidget()
        {
            if (_positionWidget != null && !_positionWidget.IsDisposed)
            {
                if (_positionWidget.Visible)
                {
                    _positionWidget.Hide();
                    if (_toggleWidgetItem != null) _toggleWidgetItem.Text = "Show Positions";
                }
                else
                {
                    _positionWidget.Show();
                    _positionWidget.BringToFront();
                    if (_toggleWidgetItem != null) _toggleWidgetItem.Text = "Hide Positions";
                }
            }
        }
        
        private void ToggleConsole()
        {
            Program.ToggleConsole();
            if (_toggleConsoleItem != null)
                _toggleConsoleItem.Text = Program.IsConsoleVisible ? "Hide Console" : "Show Console";
        }

        private async System.Threading.Tasks.Task StartAsync()
        {
            try
            {
                LoadConfig();
                await StartWebSocketServerAsync();
                await StartScraperAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nStack: {ex.StackTrace}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void LoadConfig()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    _config = JsonConvert.DeserializeObject<AppConfig>(json);
                    
                    if (!string.IsNullOrEmpty(_config?.WebSocketToken))
                        Console.WriteLine("[CONFIG] Token authentication enabled");
                    if (!string.IsNullOrEmpty(_config?.OpenAiApiKey))
                        Console.WriteLine("[CONFIG] OpenAI API key configured");
                        
                    Console.WriteLine($"[CONFIG] Quick Gainer: {_config?.QuickGainerThreshold}% | Explosion: {_config?.ExplosionThreshold}%");
                    Console.WriteLine($"[CONFIG] Auto-Close: {(_config?.AutoCloseEnabled == true ? $"ON at {_config?.AutoCloseThreshold}%" : "OFF")}");
                }
                else
                {
                    _config = new AppConfig();
                    Console.WriteLine("[CONFIG] No config.json - using defaults");
                }
                
                _analysisService = new CoinAnalysisService(_config?.OpenAiApiKey);
                _analysisService.OnLog += (msg) => Console.WriteLine(msg);
                
                // Wire up closed positions store logging
                _closedPositionsStore.OnLog += (msg) => Console.WriteLine(msg);
                
                // Wire up portfolio store logging
                _portfolioStore.OnLog += (msg) => Console.WriteLine(msg);
            }
            catch (Exception ex)
            {
                _config = new AppConfig();
                Console.WriteLine($"[CONFIG] Error loading config: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task StartWebSocketServerAsync()
        {
            try
            {
                Console.WriteLine("🔌 Starting WebSocket server...");
                
                _webSocketServer = new BinanceWebSocketManager(WEBSOCKET_PORT, _config?.WebSocketToken);

                _webSocketServer.OnLog += (msg) => Console.WriteLine(msg);
                _webSocketServer.OnError += (error) => Console.WriteLine(error);

                _webSocketServer.OnClientCountChanged += (count) =>
                {
                    var authStatus = _webSocketServer.RequiresAuth ? " 🔒" : "";
                    Console.WriteLine($"WebSocket:{authStatus} port {WEBSOCKET_PORT} - {count} clients");
                };

                _webSocketServer.OnRefreshRequested += () =>
                {
                    Console.WriteLine("[REFRESH] Command received from mobile app");
                    _scraper?.RequestRefresh();
                };
                
                _webSocketServer.OnRestartRequested += () =>
                {
                    Console.WriteLine("[RESTART] Command received from mobile app");
                    if (this.InvokeRequired)
                        this.Invoke(new Action(() => RestartScraper()));
                    else
                        RestartScraper();
                };
                
                _webSocketServer.OnAnalyzeRequested += async (symbol) =>
                {
                    Console.WriteLine($"[ANALYZE] Request for {symbol}");
                    await AnalyzePositionAsync(symbol);
                };
                
                _webSocketServer.OnPortfolioAnalysisRequested += async () =>
                {
                    Console.WriteLine($"[PORTFOLIO] Analysis requested for {_lastPositions.Count} positions");
                    await AnalyzePortfolioAsync();
                };
                
                _webSocketServer.OnClickTPSLRequested += async (trader, symbol, size) =>
                {
                    Console.WriteLine($"[TP/SL] WebSocket request for {trader} - {symbol} (size: {size})");
                    if (_scraper != null)
                    {
                        return await _scraper.ClickTPSLButtonAsync(trader, symbol, size);
                    }
                    return false;
                };
                
                _webSocketServer.OnClosePositionRequested += async (trader, symbol, size) =>
                {
                    Console.WriteLine($"[CLOSE] WebSocket request for {trader} - {symbol} (size: {size})");
                    if (_scraper != null)
                    {
                        return await _scraper.ClickClosePositionAsync(trader, symbol, size);
                    }
                    return false;
                };
                
                _webSocketServer.OnCloseModalRequested += async (trader) =>
                {
                    Console.WriteLine($"[MODAL] Close modal request for {trader}");
                    if (_scraper != null)
                    {
                        return await _scraper.CloseModalAsync(trader);
                    }
                    return false;
                };
                
                _webSocketServer.OnGetAvgPnLRequested += (uniqueKey) =>
                {
                    Console.WriteLine($"[AVG PNL] Request for {uniqueKey}");
                    return Get1HourAvgPnL(uniqueKey);
                };
                
                // Portfolio tracking events
                _webSocketServer.OnGetPortfolioRequested += () =>
                {
                    Console.WriteLine("[PORTFOLIO] Get portfolio data requested");
                    return _portfolioStore.GetPortfolio();
                };
                
                _webSocketServer.OnUpdateInitialValueRequested += (value, date) =>
                {
                    Console.WriteLine($"[PORTFOLIO] Update initial value: {value} USDT ({date:yyyy-MM-dd})");
                    _portfolioStore.UpdateInitialValue(value, date);
                };
                
                _webSocketServer.OnAddGrowthUpdateRequested += (value, notes, date) =>
                {
                    Console.WriteLine($"[PORTFOLIO] Add growth update: {value} USDT (date: {date:yyyy-MM-dd HH:mm})");
                    _portfolioStore.AddGrowthUpdate(value, notes, date);
                };
                
                _webSocketServer.OnUpdateCurrentValueRequested += (value) =>
                {
                    Console.WriteLine($"[PORTFOLIO] Update current value: {value} USDT");
                    _portfolioStore.UpdateCurrentValue(value);
                };
                
                _webSocketServer.OnAddWithdrawalRequested += (amount, category, description, currency) =>
                {
                    Console.WriteLine($"[PORTFOLIO] Add withdrawal: {amount} {currency} ({category})");
                    _portfolioStore.AddWithdrawal(amount, category, description, currency);
                };
                
                _webSocketServer.OnUpdateWithdrawalRequested += (id, amount, category, description, currency) =>
                {
                    Console.WriteLine($"[PORTFOLIO] Update withdrawal: {id}");
                    return _portfolioStore.UpdateWithdrawal(id, amount, category, description, currency);
                };
                
                _webSocketServer.OnDeleteWithdrawalRequested += (id) =>
                {
                    Console.WriteLine($"[PORTFOLIO] Delete withdrawal: {id}");
                    return _portfolioStore.DeleteWithdrawal(id);
                };

                bool started = await _webSocketServer.StartAsync();
                
                if (started)
                {
                    var authStatus = _webSocketServer.RequiresAuth ? " 🔒" : "";
                    Console.WriteLine($"WebSocket:{authStatus} port {WEBSOCKET_PORT} ready");
                }
                else
                {
                    Console.WriteLine("WebSocket server failed to start");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task StartScraperAsync()
        {
            try
            {
                _scraper = new BinanceScraperManager();

                _scraper.OnLog += (msg) => Console.WriteLine(msg);

                _scraper.OnPositionsUpdated += (positions) =>
                {
                    if (this.InvokeRequired)
                        this.Invoke(new Action(() => DisplayScrapedPositions(positions)));
                    else
                        DisplayScrapedPositions(positions);
                };

                _scraper.OnError += (error) =>
                {
                    Console.WriteLine($"[ERROR] {error}");
                };

                bool started = await _scraper.StartAsync();

                if (!started)
                {
                    Console.WriteLine("Failed to start scraper!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void DisplayScrapedPositions(List<ScrapedPosition> positions)
        {
            _lastPositions = positions;
            
            // Track PnL history for each position
            TrackPnLHistory(positions);
            
            // Check for auto-close opportunities (only positive PnL positions)
            _ = CheckAutoClose(positions);

            _trayIcon.Text = $"Copy Trading: {positions.Count} positions";

            if (_positionWidget == null || _positionWidget.IsDisposed)
            {
                _positionWidget = new PositionWidget();
                _positionWidget.OnTPSLClickRequested += async (trader, symbol, size) =>
                {
                    Console.WriteLine($"[TP/SL] Widget click for {trader} - {symbol} (size: {size})");
                    if (_scraper != null)
                    {
                        await _scraper.ClickTPSLButtonAsync(trader, symbol, size);
                    }
                };
                
                _positionWidget.OnCloseModalRequested += async (trader) =>
                {
                    Console.WriteLine($"[MODAL] Widget close modal for {trader}");
                    if (_scraper != null)
                    {
                        await _scraper.CloseModalAsync(trader);
                    }
                };
                _positionWidget.Show();
                _positionWidget.BringToFront();
                if (_toggleWidgetItem != null) _toggleWidgetItem.Text = "Hide Positions";
            }

            var positionDataList = positions.Select(p => new PositionData
            {
                Symbol = $"{p.Trader} | {p.Symbol}",
                PositionSide = p.Side,
                PositionAmt = p.Size,
                EntryPrice = "0",
                MarkPrice = "0",
                UnRealizedProfit = p.PnLRaw,  // Use raw scraped value directly
                Leverage = p.Margin
            }).ToList();

            _positionWidget.UpdatePositions(positionDataList, 0, 0, 0);

            _ = BroadcastToWebSocketAsync(positions);
        }

        private async System.Threading.Tasks.Task BroadcastToWebSocketAsync(List<ScrapedPosition> positions)
        {
            try
            {
                if (_webSocketServer != null)
                    await _webSocketServer.BroadcastPositionsAsync(positions);
            }
            catch { }
        }
        
        private void TrackPnLHistory(List<ScrapedPosition> positions)
        {
            var now = DateTime.Now;
            var cutoff = now.AddMinutes(-PNL_HISTORY_MAX_MINUTES);
            
            foreach (var pos in positions)
            {
                var key = $"{pos.Trader}_{pos.Symbol}_{pos.Side}_{pos.Size}";
                
                if (!_pnlHistory.ContainsKey(key))
                    _pnlHistory[key] = new List<(DateTime, decimal, decimal)>();
                
                _pnlHistory[key].Add((now, pos.PnL, pos.PnLPercentage));
                
                // Remove old entries
                _pnlHistory[key] = _pnlHistory[key].Where(x => x.Time > cutoff).ToList();
            }
            
            // Clean up positions that no longer exist
            var currentKeys = positions.Select(p => $"{p.Trader}_{p.Symbol}_{p.Side}_{p.Size}").ToHashSet();
            var keysToRemove = _pnlHistory.Keys.Where(k => !currentKeys.Contains(k)).ToList();
            foreach (var key in keysToRemove)
                _pnlHistory.Remove(key);
        }
        
        public (bool Success, decimal AvgPnL, decimal AvgPnLPercent, int DataPoints, string Message) Get1HourAvgPnL(string uniqueKey)
        {
            if (!_pnlHistory.ContainsKey(uniqueKey) || _pnlHistory[uniqueKey].Count == 0)
                return (false, 0, 0, 0, "No history data");
            
            var cutoff = DateTime.Now.AddHours(-1);
            var recent = _pnlHistory[uniqueKey].Where(x => x.Time > cutoff).ToList();
            
            if (recent.Count == 0)
                return (false, 0, 0, 0, "No recent data");
            
            var avgPnL = recent.Average(x => x.PnL);
            var avgPnLPercent = recent.Average(x => x.PnLPercent);
            
            return (true, avgPnL, avgPnLPercent, recent.Count, "OK");
        }
        
        // ===== AUTO-CLOSE SYSTEM =====
        
        private async Task CheckAutoClose(List<ScrapedPosition> positions)
        {
            if (_scraper == null || _config == null || !_config.AutoCloseEnabled) 
                return;
            
            var threshold = _config.AutoCloseThreshold;
            
            foreach (var pos in positions)
            {
                // ===== SAFETY: NEVER CLOSE RED POSITIONS =====
                if (pos.PnL <= 0 || pos.PnLPercentage <= 0)
                    continue;
                
                // Only close if above threshold
                if (pos.PnLPercentage < threshold)
                    continue;
                
                // Generate unique position key (hash based on trader+symbol+side+size, NOT PnL)
                var positionKey = ClosedPositionRecord.GenerateKey(pos);
                
                // Skip if already in progress or already closed in this session
                if (_autoCloseInProgress.Contains(positionKey))
                    continue;
                
                // Mark as in progress to prevent double-close
                _autoCloseInProgress.Add(positionKey);
                
                var alertType = pos.PnLPercentage >= _config.ExplosionThreshold ? "🚀 EXPLOSION" : "🎯 THRESHOLD";
                Console.WriteLine($"[AUTO-CLOSE] {alertType}: {pos.Trader} | {pos.Symbol} at {pos.PnLPercentage:+0.00;-0.00}% ({pos.PnL:+0.00;-0.00} {pos.PnLCurrency})");
                
                try
                {
                    // Step 1: Click Close Position to open modal
                    var clicked = await _scraper.ClickClosePositionAsync(pos.Trader, pos.Symbol, pos.Size);
                    
                    if (!clicked)
                    {
                        Console.WriteLine($"[AUTO-CLOSE] ⚠️ Could not click Close Position for {pos.Symbol}");
                        _autoCloseInProgress.Remove(positionKey);
                        continue;
                    }
                    
                    // Step 2: Wait for modal to appear and confirm
                    await Task.Delay(800);
                    var confirmed = await _scraper.ConfirmClosePositionAsync(pos.Trader);
                    
                    if (confirmed)
                    {
                        Console.WriteLine($"[AUTO-CLOSE] ✅ Closed {pos.Symbol} at {pos.PnLPercentage:+0.00;-0.00}% ({pos.PnL:+0.00;-0.00} {pos.PnLCurrency})");
                        
                        // Record the close
                        var record = new ClosedPositionRecord
                        {
                            PositionKey = positionKey,
                            Trader = pos.Trader,
                            Symbol = pos.Symbol,
                            Side = pos.Side,
                            Size = pos.Size,
                            PnL = pos.PnL,
                            PnLPercent = pos.PnLPercentage,
                            Currency = pos.PnLCurrency,
                            ClosedAt = DateTime.Now,
                            Reason = pos.PnLPercentage >= _config.ExplosionThreshold ? "explosion" : "threshold"
                        };
                        
                        _closedPositionsStore.AddRecord(record);
                        
                        // Log summary
                        Console.WriteLine($"[AUTO-CLOSE] 📊 {_closedPositionsStore.GetSummary()}");
                        
                        // Broadcast to MAUI app
                        if (_webSocketServer != null)
                        {
                            await _webSocketServer.BroadcastMessageAsync(new
                            {
                                type = "position_auto_closed",
                                id = record.Id,
                                positionKey = record.PositionKey,
                                trader = record.Trader,
                                symbol = record.Symbol,
                                side = record.Side,
                                size = record.Size,
                                pnl = record.PnL,
                                pnlPercent = record.PnLPercent,
                                currency = record.Currency,
                                closedAt = record.ClosedAt.ToString("o"),
                                reason = record.Reason,
                                todayTotal = _closedPositionsStore.GetTodayPnL(),
                                allTimeTotal = _closedPositionsStore.GetAllTimePnL()
                            });
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[AUTO-CLOSE] ⚠️ Could not confirm close for {pos.Symbol} - closing modal");
                        await _scraper.CloseModalAsync(pos.Trader);
                        _autoCloseInProgress.Remove(positionKey);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AUTO-CLOSE] ❌ Error: {ex.Message}");
                    _autoCloseInProgress.Remove(positionKey);
                }
            }
        }
        
        private void ShowClosedPositionsSummary()
        {
            var summary = _closedPositionsStore.GetSummary();
            var todayRecords = _closedPositionsStore.GetToday();
            
            var details = todayRecords.Count > 0 
                ? string.Join("\n", todayRecords.Take(15).Select(r => 
                    $"  {r.ClosedAt:HH:mm} | {r.Symbol}: {r.PnL:+0.00;-0.00} ({r.PnLPercent:+0.00;-0.00}%) {(r.WasEdited ? "✏️" : "")}"))
                : "  No positions closed today";
            
            MessageBox.Show(
                $"{summary}\n\n📅 Today's closes:\n{details}",
                "📊 Closed Positions Summary",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                try { _scraper?.Stop(); } catch { }
                try { _webSocketServer?.Stop(); } catch { }
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _scraper?.Stop(); } catch { }
                try { _webSocketServer?.Dispose(); } catch { }
                try { _trayIcon?.Dispose(); } catch { }
                try { _positionWidget?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
