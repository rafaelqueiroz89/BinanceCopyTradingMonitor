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
        public decimal QuickGainerThreshold { get; set; } = 10m;  // Alert when grown 10%
        public decimal ExplosionThreshold { get; set; } = 20m;    // Alert when grown 20%
    }
    
    public class CopyTradingScraperApp : Form
    {
        private NotifyIcon _trayIcon = new NotifyIcon();
        private BinanceScraperManager? _scraper;
        private BinanceWebSocketManager? _webSocketServer;
        private PositionTracker? _positionTracker;
        private PositionWidget? _positionWidget;
        private AppConfig? _config;
        private ToolStripMenuItem? _toggleWidgetItem;
        private ToolStripMenuItem? _toggleConsoleItem;

        private const int WEBSOCKET_PORT = 8765;

        public CopyTradingScraperApp()
        {
            // Completely hidden form - only tray icon
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
            // Use a chart icon from shell32.dll (icon index 21 = chart/graph)
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
            
            // Stop current scraper
            try
            {
                _scraper?.Stop();
                Console.WriteLine("[RESTART] Scraper stopped");
            }
            catch { }
            
            // Kill all Chrome processes
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
            
            // Start fresh scraper
            Console.WriteLine("[RESTART] Starting fresh scraper...");
            await StartScraperAsync();
            Console.WriteLine("[RESTART] Scraper restarted!");
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
                InitializePositionTracker();
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
                        
                    Console.WriteLine($"[CONFIG] Quick Gainer alert at: {_config?.QuickGainerThreshold}% growth");
                    Console.WriteLine($"[CONFIG] Explosion alert at: {_config?.ExplosionThreshold}% growth");
                }
                else
                {
                    _config = new AppConfig();
                    Console.WriteLine("[CONFIG] No config.json found - using defaults");
                    Console.WriteLine($"[CONFIG] Quick Gainer alert at: {_config.QuickGainerThreshold}% growth");
                    Console.WriteLine($"[CONFIG] Explosion alert at: {_config.ExplosionThreshold}% growth");
                }
            }
            catch (Exception ex)
            {
                _config = new AppConfig();
                Console.WriteLine($"[CONFIG] Error loading config: {ex.Message}");
            }
        }

        private void InitializePositionTracker()
        {
            _positionTracker = new PositionTracker
            {
                QuickGainerThreshold = _config?.QuickGainerThreshold ?? 10m,
                ExplosionThreshold = _config?.ExplosionThreshold ?? 20m
            };

            _positionTracker.OnLog += (msg) => Console.WriteLine(msg);
            
            _positionTracker.OnQuickGainer += async (alert) =>
            {
                // Windows notification
                ShowQuickGainerNotification(alert);
                
                // WebSocket broadcast
                if (_webSocketServer != null)
                {
                    await _webSocketServer.BroadcastMessageAsync(new
                    {
                        type = "quick_gainer",
                        alertType = alert.AlertType,
                        trader = alert.Trader,
                        symbol = alert.Symbol,
                        pnl = alert.PnL,
                        pnlPercentage = alert.CurrentPnLPercentage,
                        growth = alert.Growth,
                        message = alert.Message,
                        timestamp = DateTime.UtcNow
                    });
                }
            };
        }

        private void ShowQuickGainerNotification(QuickGainerAlert alert)
        {
            var icon = alert.AlertType == "explosion" ? "🚀" : "🔥";
            var title = alert.AlertType == "explosion" ? "EXPLOSION!" : "Growing Fast";
            
            _trayIcon.BalloonTipTitle = $"{icon} {title}";
            _trayIcon.BalloonTipText = $"{alert.Trader} | {alert.Symbol}\nGrew {alert.Growth:+0.00}% → now at {alert.CurrentPnLPercentage:+0.00}%";
            _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(10000);
            
            Console.WriteLine($"\n{alert.Message}\n");
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
            // Update position tracker (detects quick gainers)
            _positionTracker?.UpdatePositions(positions);

            foreach (var pos in positions)
                CheckPnLAndNotify(pos);

            _trayIcon.Text = $"Copy Trading: {positions.Count} positions";

            if (_positionWidget == null || _positionWidget.IsDisposed)
            {
                _positionWidget = new PositionWidget();
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
                UnRealizedProfit = $"{p.PnL:+0.00;-0.00} ({p.PnLPercentage:+0.00;-0.00}%)",
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

        private HashSet<string> _notifiedPositions = new HashSet<string>();

        private void CheckPnLAndNotify(ScrapedPosition pos)
        {
            try
            {
                var pnlValue = pos.PnL;
                var pnlDisplay = $"{pos.PnL:+0.00;-0.00} {pos.PnLCurrency} ({pos.PnLPercentage:+0.00;-0.00}%)";
                var positionKey = $"{pos.Trader}|{pos.Symbol}";
                var alertKey = "";

                if (pnlValue >= 30)
                {
                    alertKey = $"{positionKey}|PROFIT";
                    if (!_notifiedPositions.Contains(alertKey))
                    {
                        _notifiedPositions.Add(alertKey);
                        ShowNotification($"{pos.Trader} - {pos.Symbol}", $"PROFIT: {pnlDisplay}", ToolTipIcon.Info, true);
                        _ = _webSocketServer?.BroadcastAlertAsync($"{pos.Trader} - {pos.Symbol}", $"PROFIT: {pnlDisplay}", true);
                    }
                }
                else if (pnlValue < -100)
                {
                    alertKey = $"{positionKey}|LOSS";
                    if (!_notifiedPositions.Contains(alertKey))
                    {
                        _notifiedPositions.Add(alertKey);
                        ShowNotification($"{pos.Trader} - {pos.Symbol}", $"LOSS: {pnlDisplay}", ToolTipIcon.Warning, false);
                        _ = _webSocketServer?.BroadcastAlertAsync($"{pos.Trader} - {pos.Symbol}", $"LOSS: {pnlDisplay}", false);
                    }
                }
                else
                {
                    _notifiedPositions.Remove($"{positionKey}|PROFIT");
                    _notifiedPositions.Remove($"{positionKey}|LOSS");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking PnL: {ex.Message}");
            }
        }

        private void ShowNotification(string title, string message, ToolTipIcon icon, bool isProfit)
        {
            _trayIcon.BalloonTipTitle = isProfit ? "💰 PROFIT ALERT" : "⚠️ LOSS ALERT";
            _trayIcon.BalloonTipText = $"{title}\n{message}";
            _trayIcon.BalloonTipIcon = icon;
            _trayIcon.ShowBalloonTip(5000);
            
            var color = isProfit ? "GREEN" : "RED";
            Console.WriteLine($"\n[{color} ALERT] {title}: {message}\n");
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
