using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace BinanceCopyTradingMonitor
{
    public class BinanceConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BinanceCopyTrading_Config.json"
        );

        public string ApiKey { get; set; } = "y6UmX1ZpYGltavNXP5q1uFyBXi2FmmtZT4IqyavBpUKkAkFc1sYRWW3g9JkceeoW";
        public string SecretKey { get; set; } = "R6L8PmcR1EO1rxfurihPe3tCpeSg6AdEQhphQD4nULPOtjEhChlgXNPYgPK0EiFE";
        public decimal AlertProfitTarget { get; set; } = 100m;
        public decimal AlertLossLimit { get; set; } = -50m;
        public Dictionary<string, string> TraderNames { get; set; } = new Dictionary<string, string>();

        public static BinanceConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    return JsonConvert.DeserializeObject<BinanceConfig>(json) ?? new BinanceConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao carregar config: {ex.Message}");
            }

            return new BinanceConfig();
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao salvar config: {ex.Message}");
            }
        }
    }
}

