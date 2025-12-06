using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace BinanceCopyTradingMonitor
{
    public class PositionWidget : Form
    {
        private ListView _listView;
        private Label _summaryLabel;
        private System.Windows.Forms.Timer _updateTimer;
        private Point _dragStart;
        private bool _isDragging;

        public PositionWidget()
        {
            InitializeUI();
            _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _updateTimer.Tick += (s, e) => UpdatePnLColors();
            _updateTimer.Start();
        }

        private void InitializeUI()
        {
            // Window settings
            this.Text = "Copy Trading Monitor";
            this.Size = new Size(1200, 400);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - 870, 50);
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(20, 20, 30);
            this.ForeColor = Color.White;
            this.ShowInTaskbar = false;

            // ListView for positions
            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Color.FromArgb(25, 25, 40),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None
            };

            _listView.Columns.Add("Trader", 200);
            _listView.Columns.Add("Symbol", 150);
            _listView.Columns.Add("Size", 200, HorizontalAlignment.Right);
            _listView.Columns.Add("Margin", 250, HorizontalAlignment.Right);
            _listView.Columns.Add("PnL", 300, HorizontalAlignment.Right);

            this.Controls.Add(_listView);

            // Summary Footer
            _summaryLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(30, 30, 45),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Waiting for data..."
            };
            this.Controls.Add(_summaryLabel);
        }

        public void UpdatePositions(List<PositionData> positions, decimal totalPnL, decimal totalBalance, decimal roi)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdatePositions(positions, totalPnL, totalBalance, roi)));
                return;
            }
            
            _listView.Items.Clear();

            // Pure data from scraper
            foreach (var pos in positions)
            {
                // Extract trader from Symbol (format: "Trader | XRPUSDT")
                var parts = pos.Symbol.Split('|');
                var trader = parts.Length > 0 ? parts[0].Trim() : "";
                var symbol = parts.Length > 1 ? parts[1].Trim() : pos.Symbol;
                
                var item = new ListViewItem(trader);
                item.SubItems.Add(symbol);
                item.SubItems.Add(pos.PositionAmt);
                item.SubItems.Add(pos.Leverage);
                item.SubItems.Add(pos.UnRealizedProfit);
                
                item.ForeColor = Color.White;
                item.Font = new Font("Consolas", 11, FontStyle.Bold);
                
                _listView.Items.Add(item);
            }

            // Update simple summary
            _summaryLabel.Text = $"Positions: {positions.Count}  |  {DateTime.Now:HH:mm:ss}";
            _summaryLabel.ForeColor = Color.FromArgb(255, 215, 0);
        }

        private void UpdatePnLColors()
        {
            // Subtle animation (optional)
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }
    }
}
