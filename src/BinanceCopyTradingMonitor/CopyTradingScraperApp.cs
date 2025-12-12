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
        private TextBox _textBox = new TextBox();
        private Label _statusLabel = new Label();
        private Label _wsStatusLabel = new Label();
        private PositionWidget? _positionWidget;
        private AppConfig? _config;

        private const int WEBSOCKET_PORT = 8765;

        public CopyTradingScraperApp()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            
            InitializeUI();
            _ = StartAsync();
        }

        private void InitializeUI()
        {
            this.Text = "Copy Trading Monitor - Scraper Mode";
            this.Size = new Size(850, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(20, 20, 30);
            this.TopMost = true;

            _wsStatusLabel.Text = "WebSocket: Starting...";
            _wsStatusLabel.Dock = DockStyle.Top;
            _wsStatusLabel.Height = 25;
            _wsStatusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _wsStatusLabel.ForeColor = Color.FromArgb(100, 200, 255);
            _wsStatusLabel.BackColor = Color.FromArgb(25, 25, 35);
            _wsStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(_wsStatusLabel);

            _statusLabel.Text = "Initializing scraper...";
            _statusLabel.Dock = DockStyle.Top;
            _statusLabel.Height = 40;
            _statusLabel.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            _statusLabel.ForeColor = Color.White;
            _statusLabel.BackColor = Color.FromArgb(30, 30, 45);
            _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(_statusLabel);

            _textBox.Multiline = true;
            _textBox.Dock = DockStyle.Fill;
            _textBox.Font = new Font("Consolas", 10);
            _textBox.BackColor = Color.FromArgb(25, 25, 40);
            _textBox.ForeColor = Color.White;
            _textBox.ReadOnly = true;
            _textBox.ScrollBars = ScrollBars.Vertical;
            _textBox.BorderStyle = BorderStyle.None;
            this.Controls.Add(_textBox);

            _trayIcon.Icon = SystemIcons.Information;
            _trayIcon.Text = "Copy Trading Monitor";
            _trayIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            menu.Items.Add("Exit", null, (s, e) => { Application.Exit(); });
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
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
                UpdateWsStatus("🔌 Starting WebSocket server...");
                
                _webSocketServer = new BinanceWebSocketManager(WEBSOCKET_PORT, _config?.WebSocketToken);

                _webSocketServer.OnLog += (msg) => Console.WriteLine(msg);
                _webSocketServer.OnError += (error) => Console.WriteLine($"[WS ERROR] {error}");

                _webSocketServer.OnClientCountChanged += (count) =>
                {
                    var authStatus = _webSocketServer.RequiresAuth ? " 🔒" : "";
                    var msg = $"WebSocket:{authStatus} port {WEBSOCKET_PORT} - {count} clients";
                    if (this.InvokeRequired)
                        this.Invoke(new Action(() => UpdateWsStatus(msg)));
                    else
                        UpdateWsStatus(msg);
                };

                bool started = await _webSocketServer.StartAsync();
                
                if (started)
                {
                    var authStatus = _webSocketServer.RequiresAuth ? " 🔒" : "";
                    UpdateWsStatus($"WebSocket:{authStatus} port {WEBSOCKET_PORT} - 0 clients");
                }
                else
                {
                    UpdateWsStatus("WebSocket server failed to start");
                }
            }
            catch (Exception ex)
            {
                UpdateWsStatus($"WebSocket error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task StartScraperAsync()
        {
            try
            {
                UpdateStatus("Initializing Chromium...");
                
                _scraper = new BinanceScraperManager();

                _scraper.OnLog += (msg) =>
                {
                    if (this.InvokeRequired)
                        this.Invoke(new Action(() => UpdateStatus(msg)));
                    else
                        UpdateStatus(msg);
                };

                _scraper.OnPositionsUpdated += (positions) =>
                {
                    if (this.InvokeRequired)
                        this.Invoke(new Action(() => DisplayScrapedPositions(positions)));
                    else
                        DisplayScrapedPositions(positions);
                };

                _scraper.OnError += (error) =>
                {
                    MessageBox.Show(error, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                UpdateStatus("Downloading Chromium (if necessary)...");
                bool started = await _scraper.StartAsync();

                if (!started)
                {
                    MessageBox.Show("Failed to start scraper!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nStack: {ex.StackTrace}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateStatus(string msg) => _statusLabel.Text = msg;

        private void UpdateWsStatus(string msg)
        {
            if (_wsStatusLabel.InvokeRequired)
                _wsStatusLabel.Invoke(new Action(() => _wsStatusLabel.Text = msg));
            else
                _wsStatusLabel.Text = msg;
        }

        private void DisplayScrapedPositions(List<ScrapedPosition> positions)
        {
            // Update position tracker (detects quick gainers)
            _positionTracker?.UpdatePositions(positions);

            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine($"{positions.Count} positions - {DateTime.Now:HH:mm:ss}");
            sb.AppendLine(string.Format("{0,-15} | {1,-20} | {2,-25} | {3,-25}", 
                "Trader", "Size", "Margin", "PNL (ROE)"));
            sb.AppendLine("---");

            foreach (var pos in positions)
            {
                var pnlDisplay = $"{pos.PnL:+0.00;-0.00} {pos.PnLCurrency} ({pos.PnLPercentage:+0.00;-0.00}%)";
                sb.AppendLine(string.Format("{0,-15} | {1,-20} | {2,-25} | {3,-25}", 
                    pos.Trader.Length > 15 ? pos.Trader.Substring(0, 12) + "..." : pos.Trader,
                    pos.Size.Length > 20 ? pos.Size.Substring(0, 17) + "..." : pos.Size,
                    pos.Margin.Length > 25 ? pos.Margin.Substring(0, 22) + "..." : pos.Margin,
                    pnlDisplay.Length > 25 ? pnlDisplay.Substring(0, 22) + "..." : pnlDisplay));
                
                CheckPnLAndNotify(pos);
            }

            if (_textBox.InvokeRequired)
                _textBox.Invoke(new Action(() => _textBox.Text = sb.ToString()));
            else
                _textBox.Text = sb.ToString();

            _statusLabel.Text = $"{positions.Count} positions - {DateTime.Now:HH:mm:ss}";
            _trayIcon.Text = $"Copy Trading: {positions.Count} positions";

            if (_positionWidget == null || _positionWidget.IsDisposed)
            {
                _positionWidget = new PositionWidget();
                _positionWidget.Show();
                _positionWidget.BringToFront();
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

                if (pnlValue >= 50)
                {
                    alertKey = $"{positionKey}|PROFIT";
                    if (!_notifiedPositions.Contains(alertKey))
                    {
                        _notifiedPositions.Add(alertKey);
                        ShowNotification($"{pos.Trader} - {pos.Symbol}", $"PROFIT: {pnlDisplay}", ToolTipIcon.Info, true);
                        _ = _webSocketServer?.BroadcastAlertAsync($"{pos.Trader} - {pos.Symbol}", $"PROFIT: {pnlDisplay}", true);
                    }
                }
                else if (pnlValue < -10)
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
