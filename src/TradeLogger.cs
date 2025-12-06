using System;
using System.IO;
using System.Text;

namespace BinanceCopyTradingMonitor
{
    public static class TradeLogger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BinanceCopyTrading_Log.txt"
        );

        public static void LogNewPosition(string symbol, string side, string leverage, decimal entryPrice, decimal quantity, string traderName = "")
        {
            try
            {
                bool fileExists = File.Exists(LogFilePath);

                if (!fileExists)
                {
                    var header = "BINANCE COPY TRADING - LOG DE POSIÇÕES\n\n";
                    header += "=================================================================================================================\n";
                    header += string.Format("{0,-19} | {1,-12} | {2,-6} | {3,8} | {4,12} | {5,10} | {6,-20} | {7,-20}\n",
                        "Data/Hora", "Símbolo", "Lado", "Leverage", "Entrada", "Quantidade", "Trader", "Info");
                    header += "=================================================================================================================\n";
                    File.WriteAllText(LogFilePath, header, Encoding.UTF8);
                }

                var line = string.Format("{0,-19} | {1,-12} | {2,-6} | {3,8} | {4,12:F2} | {5,10:F4} | {6,-20} | {7,-20}\n",
                    DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                    symbol,
                    side,
                    leverage + "x",
                    entryPrice,
                    quantity,
                    traderName,
                    "");

                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao logar posição: {ex.Message}");
            }
        }

        public static void LogSummary(decimal totalPnL, decimal balance, int positionsCount, decimal roi)
        {
            try
            {
                var entry = $"\n[RESUMO {DateTime.Now:dd/MM/yyyy HH:mm}] ";
                entry += $"PnL: {totalPnL:F2} USDT | ROI: {roi:F2}% | Saldo: {balance:F2} USDT | Posições: {positionsCount}\n";
                
                if (File.Exists(LogFilePath))
                    File.AppendAllText(LogFilePath, entry, Encoding.UTF8);
            }
            catch { }
        }

        public static string GetLogPath() => LogFilePath;
    }
}

