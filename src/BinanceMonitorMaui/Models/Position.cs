using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
        
        public string UniqueKey => $"{Trader}_{Symbol}_{Side}_{Size}";
        
        private string _alertIndicator = "";
        public string AlertIndicator
        {
            get => _alertIndicator;
            set { if (_alertIndicator != value) { _alertIndicator = value; OnPropertyChanged(); } }
        }

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
        
        public string? analysis { get; set; }
        public int? totalPositions { get; set; }
        public List<PositionInsightMsg>? insights { get; set; }
        
        // TP/SL click result
        public bool? success { get; set; }
        
        // Avg PnL result
        public string? uniqueKey { get; set; }
        public decimal? avgPnL { get; set; }
        public decimal? avgPnLPercent { get; set; }
        public int? dataPoints { get; set; }

        // Growth scraping result
        public string? value { get; set; }
        public string? timestamp { get; set; }
    }
    
    public class PositionInsightMsg
    {
        public string? symbol { get; set; }
        public string? trader { get; set; }
        public string? recommendation { get; set; }
        public string? insight { get; set; }
        public string? marketData { get; set; }
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
    
    public class PortfolioAnalysisResult
    {
        public string Analysis { get; set; } = "";
        public string Summary { get; set; } = "";
        public int TotalPositions { get; set; }
        public decimal TotalPnL { get; set; }
        public List<PositionInsight> Insights { get; set; } = new();
    }
    
    public class PositionInsight
    {
        public string Symbol { get; set; } = "";
        public string Trader { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public string Insight { get; set; } = "";
        public string MarketData { get; set; } = "";
    }
    
    public class PositionAlert
    {
        public string PositionKey { get; set; } = "";
        public string AlertType { get; set; } = "";
        public decimal Threshold { get; set; }
        public bool IsAbove { get; set; }
        public bool Triggered { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public string Description => AlertType switch
        {
            "pnl_percent" => $"PnL {(IsAbove ? ">" : "<")} {Threshold:+0;-0}%",
            "pnl_value" => $"PnL {(IsAbove ? ">" : "<")} {Threshold:+0;-0} USDT",
            _ => "Custom Alert"
        };
    }
    
    public class AvgPnLResult
    {
        public bool Success { get; set; }
        public string UniqueKey { get; set; } = "";
        public decimal AvgPnL { get; set; }
        public decimal AvgPnLPercent { get; set; }
        public int DataPoints { get; set; }
        public string Message { get; set; } = "";
    }
    
    public class PortfolioData
    {
        public decimal InitialValue { get; set; }
        public DateTime InitialDate { get; set; }
        public decimal CurrentValue { get; set; }
        public List<GrowthUpdate> GrowthUpdates { get; set; } = new();
        public List<Withdrawal> Withdrawals { get; set; } = new();
        
        public decimal TotalGrowth => CurrentValue - InitialValue - TotalWithdrawals;
        public decimal TotalGrowthPercent => InitialValue == 0 ? 0 : ((CurrentValue - InitialValue) / InitialValue) * 100;
        public decimal TotalWithdrawals => Withdrawals.Sum(w => w.Amount);
    }
    
    public class GrowthUpdate
    {
        public string Id { get; set; } = "";
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
        public string Notes { get; set; } = "";
    }
    
    public class Withdrawal
    {
        public string Id { get; set; } = "";
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string Currency { get; set; } = "USDT";
        
        public string CategoryDisplay => Category switch
        {
            "credit_card" => "💳 Credit Card",
            "fiat_eur" => "💶 EUR",
            "fiat_brl" => "💵 BRL",
            "fiat_other" => "💱 Fiat",
            "voucher_uber" => "🍔 Uber Eats",
            "voucher_other" => "🎫 Voucher",
            _ => Category
        };
        
        public static class Categories
        {
            public const string CreditCard = "credit_card";
            public const string FiatEur = "fiat_eur";
            public const string FiatBrl = "fiat_brl";
            public const string FiatOther = "fiat_other";
            public const string VoucherUber = "voucher_uber";
            public const string VoucherOther = "voucher_other";
        }
    }
}
