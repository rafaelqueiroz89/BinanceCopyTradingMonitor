namespace BinanceCopyTradingMonitor
{
    public class PositionData
    {
        public string Symbol { get; set; } = "";
        public string PositionSide { get; set; } = "";
        public string PositionAmt { get; set; } = "0";
        public string EntryPrice { get; set; } = "0";
        public string MarkPrice { get; set; } = "0";
        public string UnRealizedProfit { get; set; } = "0";
        public string Leverage { get; set; } = "1";
    }
}

