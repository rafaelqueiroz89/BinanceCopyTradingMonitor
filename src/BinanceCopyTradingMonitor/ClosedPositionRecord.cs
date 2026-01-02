using System;

namespace BinanceCopyTradingMonitor
{
    public class ClosedPositionRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PositionKey { get; set; } = "";  // Hash: Trader_Symbol_Side_Size
        public string Trader { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Side { get; set; } = "";
        public string Size { get; set; } = "";
        public decimal PnL { get; set; }              // Editable
        public decimal PnLPercent { get; set; }
        public string Currency { get; set; } = "USDT";
        public DateTime ClosedAt { get; set; } = DateTime.Now;
        public string Reason { get; set; } = "";      // "threshold", "explosion", "manual"
        public string Notes { get; set; } = "";       // For manual edits/comments
        public bool WasEdited { get; set; } = false;
        
        // Generate position key (hash) from properties - excludes PnL since it's dynamic
        public static string GenerateKey(string trader, string symbol, string side, string size)
        {
            return $"{trader}_{symbol}_{side}_{size}";
        }
        
        public static string GenerateKey(ScrapedPosition pos)
        {
            return $"{pos.Trader}_{pos.Symbol}_{pos.Side}_{pos.Size}";
        }
    }
}



