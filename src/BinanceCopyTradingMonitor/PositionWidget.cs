namespace BinanceCopyTradingMonitor
{
    public class PositionWidget : Form
    {
        private ListView _listView;
        private Label _summaryLabel;
        private System.Windows.Forms.Timer _updateTimer;
        private Button _tpslButton;
        private ContextMenuStrip _contextMenu;
        private string? _selectedTrader;
        private string? _selectedSymbol;
        
        public event Action<string, string, string>? OnTPSLClickRequested; // trader, symbol, size
        public event Action<string>? OnCloseModalRequested; // trader

        public PositionWidget()
        {
            InitializeUI();
            _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _updateTimer.Tick += (s, e) => UpdatePnLColors();
            _updateTimer.Start();
        }

        private void InitializeUI()
        {
            this.Text = "Copy Trading Monitor";
            this.Size = new Size(1200, 400);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - 870, 50);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(20, 20, 30);
            this.ForeColor = Color.White;
            this.ShowInTaskbar = false;
            this.MinimizeBox = true;
            this.MaximizeBox = false;

            // Context menu for right-click
            _contextMenu = new ContextMenuStrip();
            _contextMenu.BackColor = Color.FromArgb(40, 40, 60);
            _contextMenu.ForeColor = Color.White;
            var tpslMenuItem = new ToolStripMenuItem("📈 Setup TP/SL");
            tpslMenuItem.Click += (s, e) => TriggerTPSLClick();
            _contextMenu.Items.Add(tpslMenuItem);
            var closeMenuItem = new ToolStripMenuItem("❌ Close Position");
            closeMenuItem.Click += (s, e) => TriggerClosePosition();
            _contextMenu.Items.Add(closeMenuItem);

            // Bottom panel with button and summary - ADD THIS FIRST!
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.FromArgb(30, 30, 45)
            };
            
            _tpslButton = new Button
            {
                Text = "📈 Setup TP/SL",
                Width = 130,
                Height = 35,
                Location = new Point(10, 8),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _tpslButton.FlatAppearance.BorderSize = 0;
            _tpslButton.Click += (s, e) => TriggerTPSLClick();
            bottomPanel.Controls.Add(_tpslButton);
            
            var closeButton = new Button
            {
                Text = "❌ Close Position",
                Width = 130,
                Height = 35,
                Location = new Point(150, 8),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.Click += (s, e) => TriggerClosePosition();
            bottomPanel.Controls.Add(closeButton);

            _summaryLabel = new Label
            {
                Location = new Point(290, 0),
                Size = new Size(700, 50),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Select a position and click TP/SL button (or double-click / right-click)"
            };
            bottomPanel.Controls.Add(_summaryLabel);
            
            this.Controls.Add(bottomPanel);

            // ListView - ADD AFTER bottom panel so it fills remaining space
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
                BorderStyle = BorderStyle.None,
                ContextMenuStrip = _contextMenu,
                HideSelection = false,  // Keep selection visible when not focused
                MultiSelect = false
            };
            
            _listView.DoubleClick += (s, e) => TriggerTPSLClick();
            _listView.SelectedIndexChanged += OnSelectionChanged;

            _listView.Columns.Add("Trader", 200);
            _listView.Columns.Add("Symbol", 150);
            _listView.Columns.Add("Size", 200, HorizontalAlignment.Right);
            _listView.Columns.Add("Margin", 250, HorizontalAlignment.Right);
            _listView.Columns.Add("PnL", 300, HorizontalAlignment.Right);

            this.Controls.Add(_listView);
        }
        
        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            // Remember selection for restoration after refresh
            if (_listView.SelectedItems.Count > 0)
            {
                var item = _listView.SelectedItems[0];
                _selectedTrader = item.Text;
                _selectedSymbol = item.SubItems[1].Text;
            }
        }
        
        private void TriggerTPSLClick()
        {
            if (_listView.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select a position first!", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            
            var item = _listView.SelectedItems[0];
            var trader = item.Text;
            var symbol = item.SubItems[1].Text;
            var size = item.SubItems[2].Text; // Size is in column 3 (index 2)
            
            if (!string.IsNullOrEmpty(trader) && !string.IsNullOrEmpty(symbol))
            {
                OnTPSLClickRequested?.Invoke(trader, symbol, size);
            }
        }
        
        private void TriggerClosePosition()
        {
            if (_listView.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select a position first!", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            
            var item = _listView.SelectedItems[0];
            var trader = item.Text;
            var symbol = item.SubItems[1].Text;
            var size = item.SubItems[2].Text; // Size is in column 3 (index 2)
            
            if (!string.IsNullOrEmpty(trader) && !string.IsNullOrEmpty(symbol))
            {
                // First open the TP/SL modal
                OnTPSLClickRequested?.Invoke(trader, symbol, size);
                
                // Then show confirmation
                var result = MessageBox.Show(
                    $"TP/SL modal opened for {symbol} ({size}).\n\nDo you want to keep it open to make changes?\n\nYES = Keep modal open\nNO = Close the modal",
                    "TP/SL Modal",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                    
                if (result == DialogResult.No)
                {
                    // Close the modal
                    OnCloseModalRequested?.Invoke(trader);
                }
            }
        }

        public void UpdatePositions(List<PositionData> positions, decimal totalPnL, decimal totalBalance, decimal roi)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdatePositions(positions, totalPnL, totalBalance, roi)));
                return;
            }
            
            // Remember current selection
            var prevTrader = _selectedTrader;
            var prevSymbol = _selectedSymbol;
            
            _listView.BeginUpdate();
            _listView.Items.Clear();

            int indexToSelect = -1;
            int currentIndex = 0;

            foreach (var pos in positions)
            {
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
                
                // Check if this was our previously selected item
                if (trader == prevTrader && symbol == prevSymbol)
                {
                    indexToSelect = currentIndex;
                }
                currentIndex++;
            }
            
            // Restore selection
            if (indexToSelect >= 0 && indexToSelect < _listView.Items.Count)
            {
                _listView.Items[indexToSelect].Selected = true;
                _listView.Items[indexToSelect].Focused = true;
            }
            
            _listView.EndUpdate();

            _summaryLabel.Text = $"📊 {positions.Count} positions  |  {DateTime.Now:HH:mm:ss}  |  Click row → then TP/SL button";
            _summaryLabel.ForeColor = Color.FromArgb(255, 215, 0);
        }

        private void UpdatePnLColors()
        {
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }
        
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
                this.Hide();
            }
        }
    }
}
