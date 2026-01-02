namespace BinanceMonitorMaui;

public partial class AnalysisPage : ContentPage
{
    public AnalysisPage(string summary, string analysis, string insights, int positionCount, decimal totalPnL)
    {
        InitializeComponent();
        
        SummaryLabel.Text = summary;
        AnalysisLabel.Text = analysis;
        InsightsLabel.Text = insights;
        PositionCountLabel.Text = positionCount.ToString();
        TotalPnLLabel.Text = $"{totalPnL:+0.00;-0.00} USDT";
        TotalPnLLabel.TextColor = totalPnL < 0 ? Color.FromArgb("#e94560") : Color.FromArgb("#4ade80");
    }
}
