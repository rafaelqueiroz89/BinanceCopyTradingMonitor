using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BinanceCopyTradingMonitor
{
    public class CopyTradingScraperApp : Form
    {
        private NotifyIcon _trayIcon = new NotifyIcon();
        private BinanceScraperManager? _scraper;
        private BinanceWebSocketManager? _webSocketServer;
        private TextBox _textBox = new TextBox();
        private Label _statusLabel = new Label();
        private Label _wsStatusLabel = new Label();
        private PositionWidget? _positionWidget;

        private const int WEBSOCKET_PORT = 8765;

        public CopyTradingScraperApp()
        {
            // Hide main form (only show widget)
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

            // WebSocket Status
            _wsStatusLabel.Text = "WebSocket: Starting...";
            _wsStatusLabel.Dock = DockStyle.Top;
            _wsStatusLabel.Height = 25;
            _wsStatusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _wsStatusLabel.ForeColor = Color.FromArgb(100, 200, 255);
            _wsStatusLabel.BackColor = Color.FromArgb(25, 25, 35);
            _wsStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(_wsStatusLabel);

            // Status
            _statusLabel.Text = "Initializing scraper...";
            _statusLabel.Dock = DockStyle.Top;
            _statusLabel.Height = 40;
            _statusLabel.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            _statusLabel.ForeColor = Color.White;
            _statusLabel.BackColor = Color.FromArgb(30, 30, 45);
            _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(_statusLabel);

            // TextBox with simple table
            _textBox.Multiline = true;
            _textBox.Dock = DockStyle.Fill;
            _textBox.Font = new Font("Consolas", 10);
            _textBox.BackColor = Color.FromArgb(25, 25, 40);
            _textBox.ForeColor = Color.White;
            _textBox.ReadOnly = true;
            _textBox.ScrollBars = ScrollBars.Vertical;
            _textBox.BorderStyle = BorderStyle.None;
            this.Controls.Add(_textBox);

            // Tray Icon
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
                // Start WebSocket SERVER first
                await StartWebSocketServerAsync();
                
                // Then start Scraper
                await StartScraperAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nStack: {ex.StackTrace}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async System.Threading.Tasks.Task StartWebSocketServerAsync()
        {
            try
            {
                UpdateWsStatus("🔌 Starting WebSocket server...");
                
                _webSocketServer = new BinanceWebSocketManager(WEBSOCKET_PORT);

                _webSocketServer.OnLog += (msg) =>
                {
                    Console.WriteLine(msg);
                };

                _webSocketServer.OnError += (error) =>
                {
                    Console.WriteLine($"[WS ERROR] {error}");
                };

                _webSocketServer.OnClientCountChanged += (count) =>
                {
                    if (this.InvokeRequired)
                        this.Invoke(new Action(() => UpdateWsStatus($"📱 WebSocket: {count} clients connected (port {WEBSOCKET_PORT})")));
                    else
                        UpdateWsStatus($"📱 WebSocket: {count} clients connected (port {WEBSOCKET_PORT})");
                };

                bool started = await _webSocketServer.StartAsync();
                
                if (started)
                {
                    UpdateWsStatus($"✅ WebSocket server running on port {WEBSOCKET_PORT} - 0 clients");
                    Console.WriteLine($"\n📱 Android clients can connect to: ws://<YOUR_IP>:{WEBSOCKET_PORT}/\n");
                }
                else
                {
                    UpdateWsStatus("❌ WebSocket server failed to start");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ WebSocket error: {ex.Message}");
                UpdateWsStatus($"❌ WebSocket error: {ex.Message}");
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

        private void UpdateStatus(string msg)
        {
            _statusLabel.Text = msg;
        }

        private void UpdateWsStatus(string msg)
        {
            if (_wsStatusLabel.InvokeRequired)
                _wsStatusLabel.Invoke(new Action(() => _wsStatusLabel.Text = msg));
            else
                _wsStatusLabel.Text = msg;
        }

        private void DisplayScrapedPositions(List<ScrapedPosition> positions)
        {
            // Update TextBox (console)
            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine($"{positions.Count} positions - {DateTime.Now:HH:mm:ss}");
            sb.AppendLine(string.Format("{0,-15} | {1,-20} | {2,-25} | {3,-25}", 
                "Trader", "Size", "Margin", "PNL (ROE)"));
            sb.AppendLine("---");

            foreach (var pos in positions)
            {
                sb.AppendLine(string.Format("{0,-15} | {1,-20} | {2,-25} | {3,-25}", 
                    pos.Trader.Length > 15 ? pos.Trader.Substring(0, 12) + "..." : pos.Trader,
                    pos.Size.Length > 20 ? pos.Size.Substring(0, 17) + "..." : pos.Size,
                    pos.Margin.Length > 25 ? pos.Margin.Substring(0, 22) + "..." : pos.Margin,
                    pos.PnL.Length > 25 ? pos.PnL.Substring(0, 22) + "..." : pos.PnL));
                
                // Check PnL thresholds and notify
                CheckPnLAndNotify(pos);
            }

            if (_textBox.InvokeRequired)
            {
                _textBox.Invoke(new Action(() => _textBox.Text = sb.ToString()));
            }
            else
            {
                _textBox.Text = sb.ToString();
            }

            _statusLabel.Text = $"{positions.Count} positions - {DateTime.Now:HH:mm:ss}";
            _trayIcon.Text = $"Copy Trading: {positions.Count} positions";

            // Update widget
            if (_positionWidget == null || _positionWidget.IsDisposed)
            {
                _positionWidget = new PositionWidget();
                _positionWidget.Show();
                _positionWidget.BringToFront();
            }

            // Convert ScrapedPosition to PositionData
            var positionDataList = positions.Select(p => new PositionData
            {
                Symbol = $"{p.Trader} | {p.Symbol}",
                PositionSide = p.Side,
                PositionAmt = p.Size,
                EntryPrice = "0",
                MarkPrice = "0",
                UnRealizedProfit = p.PnL,
                Leverage = p.Margin
            }).ToList();

            _positionWidget.UpdatePositions(positionDataList, 0, 0, 0);

            // 🔥 BROADCAST TO WEBSOCKET CLIENTS
            _ = BroadcastToWebSocketAsync(positions);
        }

        private async System.Threading.Tasks.Task BroadcastToWebSocketAsync(List<ScrapedPosition> positions)
        {
            try
            {
                if (_webSocketServer != null)
                {
                    await _webSocketServer.BroadcastPositionsAsync(positions);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error broadcasting to WebSocket: {ex.Message}");
            }
        }

        private HashSet<string> _notifiedPositions = new HashSet<string>();

        private void CheckPnLAndNotify(ScrapedPosition pos)
        {
            try
            {
                // Parse PnL (format: "-8.97 USDT -16%" or "+5.41 USDT+11.41%")
                var pnlText = pos.PnL;
                if (string.IsNullOrEmpty(pnlText)) return;

                // Extract first number (the USDT value)
                var match = System.Text.RegularExpressions.Regex.Match(pnlText, @"([+-]?\d+\.?\d*)");
                if (!match.Success) return;

                if (!decimal.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out decimal pnlValue))
                    return;

                var positionKey = $"{pos.Trader}|{pos.Symbol}";
                var alertKey = "";

                // Check thresholds
                if (pnlValue >= 50)
                {
                    alertKey = $"{positionKey}|PROFIT";
                    if (!_notifiedPositions.Contains(alertKey))
                    {
                        _notifiedPositions.Add(alertKey);
                        ShowNotification($"{pos.Trader} - {pos.Symbol}", 
                            $"PROFIT: {pnlText}", 
                            ToolTipIcon.Info, true);
                        
                        // Broadcast alert to WebSocket clients
                        _ = _webSocketServer?.BroadcastAlertAsync($"{pos.Trader} - {pos.Symbol}", $"PROFIT: {pnlText}", true);
                    }
                }
                else if (pnlValue < -10)
                {
                    alertKey = $"{positionKey}|LOSS";
                    if (!_notifiedPositions.Contains(alertKey))
                    {
                        _notifiedPositions.Add(alertKey);
                        ShowNotification($"{pos.Trader} - {pos.Symbol}", 
                            $"LOSS: {pnlText}", 
                            ToolTipIcon.Warning, false);
                        
                        // Broadcast alert to WebSocket clients
                        _ = _webSocketServer?.BroadcastAlertAsync($"{pos.Trader} - {pos.Symbol}", $"LOSS: {pnlText}", false);
                    }
                }
                else
                {
                    // Reset notifications when PnL is between -10 and 50
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
            // Windows notification center alert
            _trayIcon.BalloonTipTitle = isProfit ? "💰 PROFIT ALERT" : "⚠️ LOSS ALERT";
            _trayIcon.BalloonTipText = $"{title}\n{message}";
            _trayIcon.BalloonTipIcon = icon;
            _trayIcon.ShowBalloonTip(5000);
            
            // Log to console
            var color = isProfit ? "GREEN" : "RED";
            Console.WriteLine($"\n[{color} ALERT] {title}: {message}\n");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _scraper?.Stop();
            _webSocketServer?.Stop();
            
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scraper?.Stop();
                _webSocketServer?.Dispose();
                _trayIcon?.Dispose();
                _positionWidget?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
