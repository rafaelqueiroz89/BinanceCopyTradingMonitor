using Microsoft.Maui.Graphics;

namespace BinanceMonitorMaui.Views
{
    public class LineChartView : IDrawable
    {
        public List<(DateTime date, decimal value)> DataPoints { get; set; } = new();
        public Color LineColor { get; set; } = Colors.Green;
        public Color GridColor { get; set; } = Color.FromArgb("#333333");
        public Color TextColor { get; set; } = Color.FromArgb("#888888");

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (DataPoints == null || DataPoints.Count < 2)
            {
                // Draw "No data" message
                canvas.FontColor = TextColor;
                canvas.FontSize = 12;
                canvas.DrawString("No data to display", dirtyRect.Center.X, dirtyRect.Center.Y, HorizontalAlignment.Center);
                return;
            }

            var width = dirtyRect.Width;
            var height = dirtyRect.Height;
            var padding = 40f;
            var chartWidth = width - padding * 2;
            var chartHeight = height - padding * 2;

            // Calculate value range
            var minValue = (float)DataPoints.Min(p => p.value);
            var maxValue = (float)DataPoints.Max(p => p.value);
            var valueRange = maxValue - minValue;
            if (valueRange == 0) valueRange = 1;

            // Calculate date range
            var minDate = DataPoints.Min(p => p.date);
            var maxDate = DataPoints.Max(p => p.date);
            var dateRange = (maxDate - minDate).TotalDays;
            if (dateRange == 0) dateRange = 1;

            // Draw grid lines
            canvas.StrokeColor = GridColor;
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = new float[] { 5, 5 };

            // Horizontal grid lines
            for (int i = 0; i <= 4; i++)
            {
                var y = padding + (chartHeight / 4) * i;
                canvas.DrawLine(padding, y, width - padding, y);
                
                // Draw value labels
                var value = maxValue - (valueRange / 4) * i;
                canvas.FontColor = TextColor;
                canvas.FontSize = 10;
                canvas.DrawString($"{value:F0}", padding - 5, y, HorizontalAlignment.Right);
            }

            // Draw line chart
            canvas.StrokeColor = LineColor;
            canvas.StrokeSize = 2;
            canvas.StrokeDashPattern = null;

            var pathF = new PathF();
            bool firstPoint = true;

            foreach (var point in DataPoints.OrderBy(p => p.date))
            {
                var x = padding + (float)(((point.date - minDate).TotalDays / dateRange) * chartWidth);
                var y = padding + chartHeight - (((float)point.value - minValue) / valueRange) * chartHeight;

                if (firstPoint)
                {
                    pathF.MoveTo(x, y);
                    firstPoint = false;
                }
                else
                {
                    pathF.LineTo(x, y);
                }
            }

            canvas.DrawPath(pathF);

            // Draw data points
            canvas.FillColor = LineColor;
            foreach (var point in DataPoints)
            {
                var x = padding + (float)(((point.date - minDate).TotalDays / dateRange) * chartWidth);
                var y = padding + chartHeight - (((float)point.value - minValue) / valueRange) * chartHeight;
                canvas.FillCircle(x, y, 4);
            }

            // Draw date labels (first and last)
            canvas.FontColor = TextColor;
            canvas.FontSize = 9;
            canvas.DrawString(minDate.ToString("MM/dd"), padding, height - padding + 15, HorizontalAlignment.Left);
            canvas.DrawString(maxDate.ToString("MM/dd"), width - padding, height - padding + 15, HorizontalAlignment.Right);
        }
    }
}
