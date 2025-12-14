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
        
        public event Action<string>? OnLog;

        public CoinAnalysisService(string? openAiApiKey)
        {
            _httpClient = new HttpClient();
            _openAiApiKey = openAiApiKey;
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

        private async Task<string> CallChatGptAsync(string prompt)
        {
            var request = new
            {
                model = "gpt-4-turbo",
                messages = new[]
                {
                    new { role = "system", content = "Crypto analyst. Be extremely brief." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 100,
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

        private void Log(string message)
        {
            OnLog?.Invoke($"[AI] {message}");
        }
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
}

