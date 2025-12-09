namespace BinanceMonitorMaui.Models
{
    public class Position
    {
        public string Trader { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Side { get; set; } = "";
        public string Size { get; set; } = "";
        public string Margin { get; set; } = "";
        public string PnL { get; set; } = "";
        
        public Color PnLColor
        {
            get
            {
                if (string.IsNullOrEmpty(PnL)) return Colors.White;
                return PnL.Contains("-") ? Color.FromArgb("#e94560") : Color.FromArgb("#4ade80");
            }
        }
    }

    public class WebSocketMessage
    {
        public string type { get; set; } = "";
        public List<Position>? data { get; set; }
        public int? count { get; set; }
        public string? title { get; set; }
        public string? message { get; set; }
        public bool? isProfit { get; set; }
    }
}
