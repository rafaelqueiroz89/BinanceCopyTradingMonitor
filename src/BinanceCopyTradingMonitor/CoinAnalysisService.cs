using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BinanceCopyTradingMonitor
{
    public class CoinAnalysisService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _openAiApiKey;
        private List<ScrapedPosition> _previousPositions = new();
        private DateTime _previousSnapshotTime = DateTime.MinValue;
        
        public event Action<string>? OnLog;

        public CoinAnalysisService(string? openAiApiKey)
        {
            _httpClient = new HttpClient();
            _openAiApiKey = openAiApiKey;
        }
        
        public void SaveSnapshot(List<ScrapedPosition> positions)
        {
            _previousPositions = positions.Select(p => new ScrapedPosition
            {
                Trader = p.Trader,
                Symbol = p.Symbol,
                Side = p.Side,
                Size = p.Size,
                Margin = p.Margin,
                PnL = p.PnL,
                PnLCurrency = p.PnLCurrency,
                PnLPercentage = p.PnLPercentage
            }).ToList();
            _previousSnapshotTime = DateTime.Now;
            Log($"Snapshot saved: {positions.Count} positions");
        }

        public async Task<List<KlineData>> GetKlineDataAsync(string symbol, string interval = "4h")
        {
            try
            {
                var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=42";
                
                var response = await _httpClient.GetStringAsync(url);
                var klines = JsonConvert.DeserializeObject<List<List<object>>>(response);
                
                var result = new List<KlineData>();
                if (klines != null)
                {
                    foreach (var k in klines)
                    {
                        result.Add(new KlineData
                        {
                            OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(k[0])).DateTime,
                            Open = Convert.ToDecimal(k[1].ToString()),
                            High = Convert.ToDecimal(k[2].ToString()),
                            Low = Convert.ToDecimal(k[3].ToString()),
                            Close = Convert.ToDecimal(k[4].ToString()),
                            Volume = Convert.ToDecimal(k[5].ToString())
                        });
                    }
                }
                
                Log($"Fetched {result.Count} klines for {symbol}");
                return result;
            }
            catch (Exception ex)
            {
                Log($"Error fetching klines for {symbol}: {ex.Message}");
                return new List<KlineData>();
            }
        }
        
        public async Task<PriceInfoResult> Get5MinAveragePriceAsync(string symbol)
        {
            try
            {
                // Normalize symbol
                var cleanSymbol = symbol.Replace("/", "").ToUpper();
                if (!cleanSymbol.EndsWith("USDT")) cleanSymbol += "USDT";
                
                // Get last 5 1-minute candles
                var url = $"https://api.binance.com/api/v3/klines?symbol={cleanSymbol}&interval=1m&limit=5";
                var response = await _httpClient.GetStringAsync(url);
                var klines = JsonConvert.DeserializeObject<List<List<object>>>(response);
                
                if (klines == null || klines.Count == 0)
                {
                    return new PriceInfoResult { Success = false, Message = "No price data" };
                }
                
                var closes = klines.Select(k => Convert.ToDecimal(k[4].ToString())).ToList();
                var highs = klines.Select(k => Convert.ToDecimal(k[2].ToString())).ToList();
                var lows = klines.Select(k => Convert.ToDecimal(k[3].ToString())).ToList();
                var volumes = klines.Select(k => Convert.ToDecimal(k[5].ToString())).ToList();
                
                var avgPrice = closes.Average();
                var currentPrice = closes.Last();
                var high5m = highs.Max();
                var low5m = lows.Min();
                var totalVolume = volumes.Sum();
                var priceChange = closes.Count >= 2 ? ((currentPrice - closes.First()) / closes.First() * 100) : 0;
                
                // Also get 24h ticker for more context
                var ticker24hUrl = $"https://api.binance.com/api/v3/ticker/24hr?symbol={cleanSymbol}";
                var ticker24hResponse = await _httpClient.GetStringAsync(ticker24hUrl);
                var ticker24h = JsonConvert.DeserializeObject<Dictionary<string, object>>(ticker24hResponse);
                
                var priceChange24h = ticker24h != null && ticker24h.ContainsKey("priceChangePercent") 
                    ? Convert.ToDecimal(ticker24h["priceChangePercent"].ToString()) 
                    : 0;
                var volume24h = ticker24h != null && ticker24h.ContainsKey("volume") 
                    ? Convert.ToDecimal(ticker24h["volume"].ToString()) 
                    : 0;
                
                Log($"5min avg price for {cleanSymbol}: {avgPrice:F6} | Current: {currentPrice:F6}");
                
                return new PriceInfoResult
                {
                    Success = true,
                    Symbol = cleanSymbol,
                    CurrentPrice = currentPrice,
                    AvgPrice5m = avgPrice,
                    High5m = high5m,
                    Low5m = low5m,
                    PriceChange5m = priceChange,
                    Volume5m = totalVolume,
                    PriceChange24h = priceChange24h,
                    Volume24h = volume24h
                };
            }
            catch (Exception ex)
            {
                Log($"Error fetching 5min avg for {symbol}: {ex.Message}");
                return new PriceInfoResult { Success = false, Message = ex.Message };
            }
        }

        public async Task<AnalysisResult> AnalyzePositionAsync(ScrapedPosition position)
        {
            if (string.IsNullOrEmpty(_openAiApiKey))
            {
                return new AnalysisResult
                {
                    Symbol = position.Symbol,
                    Recommendation = "NO_API_KEY",
                    Summary = "OpenAI API key not configured. Add 'OpenAiApiKey' to config.json",
                    Confidence = 0
                };
            }

            try
            {
                var symbol = position.Symbol.Replace("/", "").ToUpper();
                if (!symbol.EndsWith("USDT")) symbol += "USDT";
                
                var klines = await GetKlineDataAsync(symbol);
                
                if (klines.Count == 0)
                {
                    return new AnalysisResult
                    {
                        Symbol = position.Symbol,
                        Recommendation = "ERROR",
                        Summary = $"Could not fetch price data for {symbol}",
                        Confidence = 0
                    };
                }

                var prompt = BuildAnalysisPrompt(position, klines);
                var analysis = await CallChatGptAsync(prompt);
                
                return ParseAnalysisResponse(position.Symbol, analysis);
            }
            catch (Exception ex)
            {
                Log($"Error analyzing {position.Symbol}: {ex.Message}");
                return new AnalysisResult
                {
                    Symbol = position.Symbol,
                    Recommendation = "ERROR",
                    Summary = ex.Message,
                    Confidence = 0
                };
            }
        }

        private string BuildAnalysisPrompt(ScrapedPosition position, List<KlineData> klines)
        {
            var sb = new StringBuilder();
            
            var lastPrice = klines.LastOrDefault()?.Close ?? 0;
            var firstPrice = klines.FirstOrDefault()?.Close ?? 0;
            var priceChange = firstPrice > 0 ? ((lastPrice - firstPrice) / firstPrice * 100) : 0;
            var high7d = klines.Max(k => k.High);
            var low7d = klines.Min(k => k.Low);
            
            sb.AppendLine($"{position.Symbol} {position.Side} position at {position.PnLPercentage:+0.00;-0.00}% PnL");
            sb.AppendLine($"7d: {priceChange:+0.0;-0.0}% | High: {high7d:F2} | Low: {low7d:F2} | Now: {lastPrice:F2}");
            sb.AppendLine();
            sb.AppendLine("Reply ONLY in this format (max 15 words for summary):");
            sb.AppendLine("RECOMMENDATION: HOLD or CLOSE");
            sb.AppendLine("CONFIDENCE: [number]%");
            sb.AppendLine("SUMMARY: [brief reason]");
            
            return sb.ToString();
        }

        private AnalysisResult ParseAnalysisResponse(string symbol, string response)
        {
            var result = new AnalysisResult
            {
                Symbol = symbol,
                RawResponse = response
            };

            try
            {
                if (response.Contains("RECOMMENDATION:"))
                {
                    var recLine = response.Split('\n')
                        .FirstOrDefault(l => l.Contains("RECOMMENDATION:"));
                    if (recLine != null)
                    {
                        result.Recommendation = recLine.Contains("CLOSE") ? "CLOSE" : "HOLD";
                    }
                }

                if (response.Contains("CONFIDENCE:"))
                {
                    var confLine = response.Split('\n')
                        .FirstOrDefault(l => l.Contains("CONFIDENCE:"));
                    if (confLine != null)
                    {
                        var confStr = new string(confLine.Where(c => char.IsDigit(c)).ToArray());
                        if (int.TryParse(confStr, out int conf))
                        {
                            result.Confidence = Math.Min(100, Math.Max(0, conf));
                        }
                    }
                }

                if (response.Contains("SUMMARY:"))
                {
                    var idx = response.IndexOf("SUMMARY:");
                    if (idx >= 0)
                    {
                        result.Summary = response.Substring(idx + 8).Trim();
                    }
                }

                if (string.IsNullOrEmpty(result.Recommendation))
                    result.Recommendation = "HOLD";
                if (string.IsNullOrEmpty(result.Summary))
                    result.Summary = response;
            }
            catch
            {
                result.Recommendation = "HOLD";
                result.Summary = response;
            }

            Log($"Analysis for {symbol}: {result.Recommendation} ({result.Confidence}%)");
            return result;
        }

        public async Task<PortfolioAnalysisResult> AnalyzePortfolioAsync(List<ScrapedPosition> currentPositions)
        {
            if (string.IsNullOrEmpty(_openAiApiKey))
            {
                return new PortfolioAnalysisResult
                {
                    Analysis = "OpenAI API key not configured. Add 'OpenAiApiKey' to config.json",
                    Summary = "NO_API_KEY",
                    TotalPositions = currentPositions.Count
                };
            }

            try
            {
                Log($"Starting portfolio analysis for {currentPositions.Count} positions...");
                
                var marketDataTasks = currentPositions.Select(async p =>
                {
                    var symbol = p.Symbol.Replace("/", "").ToUpper();
                    if (!symbol.EndsWith("USDT")) symbol += "USDT";
                    var klines = await GetKlineDataAsync(symbol, "1h");
                    return (Position: p, Klines: klines);
                }).ToList();

                var marketData = await Task.WhenAll(marketDataTasks);
                
                var prompt = BuildPortfolioPrompt(currentPositions, _previousPositions, marketData.ToList());
                var analysis = await CallChatGptAsync(prompt, 1500);
                
                var result = ParsePortfolioResponse(analysis, currentPositions, marketData.ToList());
                
                SaveSnapshot(currentPositions);
                
                return result;
            }
            catch (Exception ex)
            {
                Log($"Error analyzing portfolio: {ex.Message}");
                return new PortfolioAnalysisResult
                {
                    Analysis = $"Error: {ex.Message}",
                    Summary = "ERROR",
                    TotalPositions = currentPositions.Count
                };
            }
        }

        private string BuildPortfolioPrompt(
            List<ScrapedPosition> current, 
            List<ScrapedPosition> previous, 
            List<(ScrapedPosition Position, List<KlineData> Klines)> marketData)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PORTFOLIO ANALYSIS REQUEST");
            sb.AppendLine("==========================");
            sb.AppendLine();
            
            var totalPnL = current.Sum(p => p.PnL);
            var avgPnLPct = current.Count > 0 ? current.Average(p => p.PnLPercentage) : 0;
            
            sb.AppendLine($"CURRENT STATE ({current.Count} positions):");
            sb.AppendLine($"Total PnL: {totalPnL:+0.00;-0.00} USDT | Avg: {avgPnLPct:+0.00;-0.00}%");
            sb.AppendLine();
            
            foreach (var (pos, klines) in marketData)
            {
                var lastPrice = klines.LastOrDefault()?.Close ?? 0;
                var priceChange24h = klines.Count >= 24 
                    ? ((lastPrice - klines[^24].Close) / klines[^24].Close * 100) 
                    : 0;
                var high24h = klines.TakeLast(24).Any() ? klines.TakeLast(24).Max(k => k.High) : 0;
                var low24h = klines.TakeLast(24).Any() ? klines.TakeLast(24).Min(k => k.Low) : 0;
                
                sb.AppendLine($"• {pos.Symbol} ({pos.Trader})");
                sb.AppendLine($"  {pos.Side} | PnL: {pos.PnL:+0.00;-0.00} ({pos.PnLPercentage:+0.00;-0.00}%)");
                sb.AppendLine($"  24h: {priceChange24h:+0.0;-0.0}% | H: {high24h:F4} | L: {low24h:F4} | Now: {lastPrice:F4}");
            }
            
            if (previous.Count > 0 && _previousSnapshotTime > DateTime.MinValue)
            {
                sb.AppendLine();
                sb.AppendLine($"COMPARISON (vs {(DateTime.Now - _previousSnapshotTime).TotalMinutes:F0}min ago):");
                
                var prevTotal = previous.Sum(p => p.PnL);
                var pnlChange = totalPnL - prevTotal;
                sb.AppendLine($"PnL Change: {pnlChange:+0.00;-0.00} USDT");
                
                var newPositions = current.Where(c => !previous.Any(p => p.Symbol == c.Symbol && p.Trader == c.Trader)).ToList();
                var closedPositions = previous.Where(p => !current.Any(c => c.Symbol == p.Symbol && c.Trader == p.Trader)).ToList();
                
                if (newPositions.Count > 0)
                    sb.AppendLine($"New: {string.Join(", ", newPositions.Select(p => p.Symbol))}");
                if (closedPositions.Count > 0)
                    sb.AppendLine($"Closed: {string.Join(", ", closedPositions.Select(p => p.Symbol))}");
            }
            
            sb.AppendLine();
            sb.AppendLine("Provide:");
            sb.AppendLine("1. Brief portfolio summary (2-3 sentences)");
            sb.AppendLine("2. For each position: [SYMBOL] [HOLD/CLOSE] - brief reason");
            sb.AppendLine("3. Overall recommendation");
            
            return sb.ToString();
        }

        private PortfolioAnalysisResult ParsePortfolioResponse(
            string response, 
            List<ScrapedPosition> positions,
            List<(ScrapedPosition Position, List<KlineData> Klines)> marketData)
        {
            var result = new PortfolioAnalysisResult
            {
                Analysis = response,
                TotalPositions = positions.Count,
                TotalPnL = positions.Sum(p => p.PnL)
            };
            
            var lines = response.Split('\n');
            var summaryLines = lines.TakeWhile(l => !l.Contains("[") || l.StartsWith("1.")).Take(5);
            result.Summary = string.Join(" ", summaryLines).Trim();
            
            foreach (var (pos, klines) in marketData)
            {
                var lastPrice = klines.LastOrDefault()?.Close ?? 0;
                var priceChange = klines.Count >= 24 
                    ? ((lastPrice - klines[^24].Close) / klines[^24].Close * 100) 
                    : 0;
                    
                var insight = new PositionInsight
                {
                    Symbol = pos.Symbol,
                    Trader = pos.Trader,
                    MarketData = $"24h: {priceChange:+0.0;-0.0}% | Now: {lastPrice:F4}"
                };
                
                var posLine = lines.FirstOrDefault(l => 
                    l.ToUpper().Contains(pos.Symbol.ToUpper()) && 
                    (l.Contains("HOLD") || l.Contains("CLOSE") || l.Contains("BUY") || l.Contains("SELL")));
                    
                if (posLine != null)
                {
                    insight.Recommendation = posLine.Contains("CLOSE") || posLine.Contains("SELL") ? "CLOSE" : "HOLD";
                    var dashIdx = posLine.IndexOf("-");
                    insight.Insight = dashIdx >= 0 ? posLine.Substring(dashIdx + 1).Trim() : posLine;
                }
                else
                {
                    insight.Recommendation = "HOLD";
                    insight.Insight = "No specific recommendation";
                }
                
                result.Insights.Add(insight);
            }
            
            Log($"Portfolio analysis complete: {result.Insights.Count} insights");
            return result;
        }

        private async Task<string> CallChatGptAsync(string prompt, int maxTokens = 100)
        {
            var request = new
            {
                model = "gpt-4-turbo",
                messages = new[]
                {
                    new { role = "system", content = "Crypto portfolio analyst. Be concise and actionable." },
                    new { role = "user", content = prompt }
                },
                max_tokens = maxTokens,
                temperature = 0.3
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAiApiKey}");
            
            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseJson = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Log($"OpenAI API error: {responseJson}");
                throw new Exception($"OpenAI API error: {response.StatusCode}");
            }

            var result = JObject.Parse(responseJson);
            var message = result["choices"]?[0]?["message"]?["content"]?.ToString();
            
            return message ?? "No response from ChatGPT";
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[AI] {message}");
        }
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

    public class KlineData
    {
        public DateTime OpenTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }

    public class AnalysisResult
    {
        public string Symbol { get; set; } = "";
        public string Recommendation { get; set; } = "HOLD";
        public int Confidence { get; set; } = 0;
        public string Summary { get; set; } = "";
        public string? RawResponse { get; set; }
    }
    
    public class PriceInfoResult
    {
        public bool Success { get; set; }
        public string Symbol { get; set; } = "";
        public string Message { get; set; } = "";
        public decimal CurrentPrice { get; set; }
        public decimal AvgPrice5m { get; set; }
        public decimal High5m { get; set; }
        public decimal Low5m { get; set; }
        public decimal PriceChange5m { get; set; }
        public decimal Volume5m { get; set; }
        public decimal PriceChange24h { get; set; }
        public decimal Volume24h { get; set; }
    }
}

