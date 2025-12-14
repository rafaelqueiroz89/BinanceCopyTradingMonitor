using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BinanceMonitorMaui.Models
{
    public class TraderGroup : ObservableCollection<Position>, INotifyPropertyChanged
    {
        private string _traderName = "";
        private decimal _totalPnL = 0;

        public string TraderName
        {
            get => _traderName;
            set { _traderName = value; OnPropertyChanged(nameof(TraderName)); }
        }

        public decimal TotalPnL
        {
            get => _totalPnL;
            set
            {
                _totalPnL = value;
                OnPropertyChanged(nameof(TotalPnL));
                OnPropertyChanged(nameof(PnLSummary));
                OnPropertyChanged(nameof(PnLColor));
            }
        }

        public string PnLSummary => $"{TotalPnL:+0.00;-0.00} USDT";
        public Color PnLColor => TotalPnL < 0 ? Color.FromArgb("#e94560") : Color.FromArgb("#4ade80");

        public TraderGroup(string traderName, IEnumerable<Position> positions) : base(positions)
        {
            TraderName = traderName;
            UpdateTotalPnL();
        }

        public void UpdateTotalPnL()
        {
            TotalPnL = this.Sum(p => p.PnL);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Position : INotifyPropertyChanged
    {
        private string _trader = "";
        private string _symbol = "";
        private string _side = "";
        private string _size = "";
        private string _margin = "";
        private string _pnlRaw = "";
        private decimal _pnl = 0;
        private string _pnlCurrency = "USDT";
        private decimal _pnlPercentage = 0;

        public string Trader
        {
            get => _trader;
            set { if (_trader != value) { _trader = value; OnPropertyChanged(); } }
        }

        public string Symbol
        {
            get => _symbol;
            set { if (_symbol != value) { _symbol = value; OnPropertyChanged(); } }
        }

        public string Side
        {
            get => _side;
            set { if (_side != value) { _side = value; OnPropertyChanged(); } }
        }

        public string Size
        {
            get => _size;
            set { if (_size != value) { _size = value; OnPropertyChanged(); } }
        }

        public string Margin
        {
            get => _margin;
            set { if (_margin != value) { _margin = value; OnPropertyChanged(); } }
        }

        public string PnLRaw
        {
            get => _pnlRaw;
            set { if (_pnlRaw != value) { _pnlRaw = value; OnPropertyChanged(); } }
        }

        public decimal PnL
        {
            get => _pnl;
            set
            {
                if (_pnl != value)
                {
                    _pnl = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PnLDisplay));
                    OnPropertyChanged(nameof(PnLColor));
                }
            }
        }

        public string PnLCurrency
        {
            get => _pnlCurrency;
            set { if (_pnlCurrency != value) { _pnlCurrency = value; OnPropertyChanged(); } }
        }

        public decimal PnLPercentage
        {
            get => _pnlPercentage;
            set
            {
                if (_pnlPercentage != value)
                {
                    _pnlPercentage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PnLDisplay));
                }
            }
        }

        public string PnLDisplay => $"{PnL:+0.00;-0.00} ({PnLPercentage:+0.00;-0.00}%)";
        
        public Color PnLColor => PnL < 0 ? Color.FromArgb("#e94560") : Color.FromArgb("#4ade80");

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void UpdateFrom(Position other)
        {
            Trader = other.Trader;
            Symbol = other.Symbol;
            Side = other.Side;
            Size = other.Size;
            Margin = other.Margin;
            PnLRaw = other.PnLRaw;
            PnLCurrency = other.PnLCurrency;
            PnLPercentage = other.PnLPercentage;
            PnL = other.PnL;
        }
    }

    public class WebSocketMessage
    {
        public string type { get; set; } = "";
        public List<Position>? data { get; set; }
        public int? count { get; set; }
        public decimal? totalPnL { get; set; }
        public decimal? totalPnLPercentage { get; set; }
        public string? title { get; set; }
        public string? message { get; set; }
        public bool? isProfit { get; set; }
        
        public string? alertType { get; set; }
        public string? trader { get; set; }
        public string? symbol { get; set; }
        public decimal? pnl { get; set; }
        
        public string? recommendation { get; set; }
        public int? confidence { get; set; }
        public string? summary { get; set; }
        public string? currentPnl { get; set; }
        public string? currentPnlPercent { get; set; }
        public decimal? pnlPercentage { get; set; }
        public decimal? growth { get; set; }
    }

    public class QuickGainerAlert
    {
        public string AlertType { get; set; } = "";
        public string Trader { get; set; } = "";
        public string Symbol { get; set; } = "";
        public decimal PnL { get; set; }
        public decimal PnLPercentage { get; set; }
        public decimal Growth { get; set; }
        public string Message { get; set; } = "";
        
        public bool IsExplosion => AlertType == "explosion";
    }
    
    public class AnalysisResult
    {
        public string Symbol { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public int Confidence { get; set; }
        public string Summary { get; set; } = "";
        public string Trader { get; set; } = "";
        public string CurrentPnl { get; set; } = "";
        public string CurrentPnlPercent { get; set; } = "";
    }
}
