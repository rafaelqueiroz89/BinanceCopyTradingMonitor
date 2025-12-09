using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;

namespace BinanceCopyTradingMonitor
{
    /// <summary>
    /// API REST específica para COPY TRADING da Binance
    /// Endpoints: /sapi/v1/copyTrading/futures/
    /// </summary>
    public class BinanceCopyTradingRestApi : IDisposable
    {
        private const string BaseUrl = "https://api.binance.com";
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _secretKey;

        public BinanceCopyTradingRestApi(string apiKey, string secretKey)
        {
            _apiKey = apiKey;
            _secretKey = secretKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);
            
            Console.WriteLine("✅ Binance Copy Trading REST API inicializada");
        }

        private long GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private string CreateSignature(string queryString)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(queryString));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// GET /sapi/v1/copyTrading/futures/userStatus
        /// Retorna o status do lead trader (se você é um lead trader)
        /// </summary>
        public async Task<string> GetUserStatusAsync()
        {
            try
            {
                long timestamp = GetTimestamp();
                string queryString = $"timestamp={timestamp}&recvWindow=5000";
                string signature = CreateSignature(queryString);
                string url = $"{BaseUrl}/sapi/v1/copyTrading/futures/userStatus?{queryString}&signature={signature}";

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"📊 User Status: {content}");
                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro em GetUserStatusAsync: {ex.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// GET /sapi/v1/copyTrading/futures/leadSymbol
        /// Retorna símbolos permitidos para lead traders
        /// </summary>
        public async Task<string> GetLeadSymbolAsync()
        {
            try
            {
                long timestamp = GetTimestamp();
                string queryString = $"timestamp={timestamp}&recvWindow=5000";
                string signature = CreateSignature(queryString);
                string url = $"{BaseUrl}/sapi/v1/copyTrading/futures/leadSymbol?{queryString}&signature={signature}";

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"📊 Lead Symbol: {content}");
                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro em GetLeadSymbolAsync: {ex.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// GET /sapi/v1/copyTrading/futures/myCopyOrders
        /// ⭐ ESSENCIAL: Retorna suas ordens copiadas
        /// </summary>
        public async Task<List<CopyOrder>> GetMyCopyOrdersAsync(string? symbol = null, int limit = 50)
        {
            try
            {
                long timestamp = GetTimestamp();
                string queryString = $"timestamp={timestamp}&recvWindow=5000&limit={limit}";
                if (!string.IsNullOrEmpty(symbol))
                {
                    queryString += $"&symbol={symbol}";
                }
                
                string signature = CreateSignature(queryString);
                string url = $"{BaseUrl}/sapi/v1/copyTrading/futures/myCopyOrders?{queryString}&signature={signature}";

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"📦 My Copy Orders Response: {content.Substring(0, Math.Min(500, content.Length))}...");

                if (response.IsSuccessStatusCode)
                {
                    var orders = JsonConvert.DeserializeObject<List<CopyOrder>>(content) ?? new List<CopyOrder>();
                    Console.WriteLine($"✅ {orders.Count} ordens copiadas encontradas");
                    return orders;
                }
                else
                {
                    Console.WriteLine($"⚠️ Erro HTTP {response.StatusCode}: {content}");
                    return new List<CopyOrder>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro em GetMyCopyOrdersAsync: {ex.Message}");
                return new List<CopyOrder>();
            }
        }

        /// <summary>
        /// GET /sapi/v1/copyTrading/futures/leadInfo
        /// ⭐ ESSENCIAL: Retorna info dos traders que você segue
        /// </summary>
        public async Task<List<LeadTraderInfo>> GetLeadInfoAsync()
        {
            try
            {
                long timestamp = GetTimestamp();
                string queryString = $"timestamp={timestamp}&recvWindow=5000";
                string signature = CreateSignature(queryString);
                string url = $"{BaseUrl}/sapi/v1/copyTrading/futures/leadInfo?{queryString}&signature={signature}";

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"👥 Lead Info Response: {content}");

                if (response.IsSuccessStatusCode)
                {
                    var leadInfo = JsonConvert.DeserializeObject<List<LeadTraderInfo>>(content) ?? new List<LeadTraderInfo>();
                    Console.WriteLine($"✅ {leadInfo.Count} lead traders encontrados");
                    
                    foreach (var lead in leadInfo)
                    {
                        Console.WriteLine($"   📌 Trader: {lead.LeadTraderNickName} (ID: {lead.PortfolioId})");
                    }
                    
                    return leadInfo;
                }
                else
                {
                    Console.WriteLine($"⚠️ Erro HTTP {response.StatusCode}: {content}");
                    return new List<LeadTraderInfo>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro em GetLeadInfoAsync: {ex.Message}");
                return new List<LeadTraderInfo>();
            }
        }

        /// <summary>
        /// GET /sapi/v1/copyTrading/futures/copyPosition
        /// ⭐⭐⭐ MAIS IMPORTANTE: Retorna suas posições copiadas ABERTAS
        /// </summary>
        public async Task<List<CopyPosition>> GetCopyPositionsAsync(string? symbol = null)
        {
            try
            {
                long timestamp = GetTimestamp();
                string queryString = $"timestamp={timestamp}&recvWindow=5000";
                if (!string.IsNullOrEmpty(symbol))
                {
                    queryString += $"&symbol={symbol}";
                }
                
                string signature = CreateSignature(queryString);
                string url = $"{BaseUrl}/sapi/v1/copyTrading/futures/copyPosition?{queryString}&signature={signature}";

                Console.WriteLine($"📡 Chamando: GET /sapi/v1/copyTrading/futures/copyPosition");
                
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"📦 Copy Position Response: {content}");

                if (response.IsSuccessStatusCode)
                {
                    var positions = JsonConvert.DeserializeObject<List<CopyPosition>>(content) ?? new List<CopyPosition>();
                    Console.WriteLine($"✅ {positions.Count} POSIÇÕES COPIADAS ABERTAS");
                    
                    foreach (var pos in positions)
                    {
                        Console.WriteLine($"   📌 {pos.Symbol}: {pos.PositionAmt} @ {pos.EntryPrice} (PnL: {pos.UnrealizedProfit})");
                    }
                    
                    return positions;
                }
                else
                {
                    Console.WriteLine($"⚠️ Erro HTTP {response.StatusCode}: {content}");
                    return new List<CopyPosition>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro em GetCopyPositionsAsync: {ex.Message}");
                return new List<CopyPosition>();
            }
        }

        /// <summary>
        /// GET /sapi/v1/copyTrading/futures/copyTradeHistory
        /// Retorna histórico de trades copiados
        /// </summary>
        public async Task<string> GetCopyTradeHistoryAsync(string? symbol = null, long? startTime = null, long? endTime = null, int limit = 50)
        {
            try
            {
                long timestamp = GetTimestamp();
                string queryString = $"timestamp={timestamp}&recvWindow=5000&limit={limit}";
                if (!string.IsNullOrEmpty(symbol)) queryString += $"&symbol={symbol}";
                if (startTime.HasValue) queryString += $"&startTime={startTime}";
                if (endTime.HasValue) queryString += $"&endTime={endTime}";
                
                string signature = CreateSignature(queryString);
                string url = $"{BaseUrl}/sapi/v1/copyTrading/futures/copyTradeHistory?{queryString}&signature={signature}";

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"📜 Copy Trade History: {content.Substring(0, Math.Min(500, content.Length))}...");
                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro em GetCopyTradeHistoryAsync: {ex.Message}");
                return "{}";
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // ==================== MODELS ====================

    public class CopyOrder
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; } = "";
        
        [JsonProperty("orderId")]
        public long OrderId { get; set; }
        
        [JsonProperty("leadTraderNickName")]
        public string LeadTraderNickName { get; set; } = "";
        
        [JsonProperty("side")]
        public string Side { get; set; } = "";
        
        [JsonProperty("origQty")]
        public string OrigQty { get; set; } = "";
        
        [JsonProperty("price")]
        public string Price { get; set; } = "";
        
        [JsonProperty("executedQty")]
        public string ExecutedQty { get; set; } = "";
        
        [JsonProperty("status")]
        public string Status { get; set; } = "";
        
        [JsonProperty("time")]
        public long Time { get; set; }
    }

    public class LeadTraderInfo
    {
        [JsonProperty("leadTraderNickName")]
        public string LeadTraderNickName { get; set; } = "";
        
        [JsonProperty("portfolioId")]
        public string PortfolioId { get; set; } = "";
        
        [JsonProperty("copyMode")]
        public string CopyMode { get; set; } = "";
        
        [JsonProperty("copyRatio")]
        public string CopyRatio { get; set; } = "";
    }

    public class CopyPosition
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; } = "";
        
        [JsonProperty("positionSide")]
        public string PositionSide { get; set; } = "";
        
        [JsonProperty("positionAmt")]
        public string PositionAmt { get; set; } = "";
        
        [JsonProperty("entryPrice")]
        public string EntryPrice { get; set; } = "";
        
        [JsonProperty("markPrice")]
        public string MarkPrice { get; set; } = "";
        
        [JsonProperty("unrealizedProfit")]
        public string UnrealizedProfit { get; set; } = "";
        
        [JsonProperty("leverage")]
        public string Leverage { get; set; } = "";
        
        [JsonProperty("leadTraderNickName")]
        public string LeadTraderNickName { get; set; } = "";
        
        [JsonProperty("updateTime")]
        public long UpdateTime { get; set; }
    }
}

