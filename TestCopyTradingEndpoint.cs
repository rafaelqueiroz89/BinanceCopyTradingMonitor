using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BinanceCopyTradingMonitor
{
    public class TestCopyTradingEndpoint : Form
    {
        private TextBox _outputBox = new TextBox();
        private Button _btnTest = new Button();

        public TestCopyTradingEndpoint()
        {
            this.Text = "Teste Copy Trading Endpoint";
            this.Size = new System.Drawing.Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;

            _outputBox.Multiline = true;
            _outputBox.Dock = DockStyle.Fill;
            _outputBox.Font = new System.Drawing.Font("Consolas", 10);
            _outputBox.ScrollBars = ScrollBars.Both;
            this.Controls.Add(_outputBox);

            _btnTest.Text = "🧪 TESTAR TODOS OS ENDPOINTS";
            _btnTest.Dock = DockStyle.Top;
            _btnTest.Height = 50;
            _btnTest.Font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold);
            _btnTest.Click += async (s, e) => await TestAllEndpoints();
            this.Controls.Add(_btnTest);
        }

        private async Task TestAllEndpoints()
        {
            _outputBox.Clear();
            Log("═══════════════════════════════════════════════════════");
            Log("🧪 TESTANDO TODOS OS ENDPOINTS DE COPY TRADING");
            Log("═══════════════════════════════════════════════════════\n");

            try
            {
                var config = BinanceConfig.Load();
                Log($"✅ Config carregado - API Key: {config.ApiKey.Substring(0, 15)}...\n");

                var api = new BinanceCopyTradingRestApi(config.ApiKey, config.SecretKey);

                // Teste 1: copyPosition (O MAIS IMPORTANTE)
                Log("\n═══════════════════════════════════════════════════════");
                Log("📊 TESTE 1: GET /sapi/v1/copyTrading/futures/copyPosition");
                Log("   (Este deve retornar suas posições abertas de Copy Trading)");
                Log("═══════════════════════════════════════════════════════");
                
                var positions = await api.GetCopyPositionsAsync();
                
                if (positions.Count > 0)
                {
                    Log($"✅ {positions.Count} POSIÇÕES ENCONTRADAS!\n");
                    foreach (var pos in positions)
                    {
                        Log($"📌 Trader: {pos.LeadTraderNickName}");
                        Log($"   Symbol: {pos.Symbol}");
                        Log($"   Side: {pos.PositionSide}");
                        Log($"   Amount: {pos.PositionAmt}");
                        Log($"   Entry: {pos.EntryPrice}");
                        Log($"   Mark: {pos.MarkPrice}");
                        Log($"   PnL: {pos.UnrealizedProfit}");
                        Log($"   Leverage: {pos.Leverage}x\n");
                    }
                }
                else
                {
                    Log("⚠️ NENHUMA POSIÇÃO RETORNADA");
                    Log("   Possíveis razões:");
                    Log("   1. Você não tem posições abertas no momento");
                    Log("   2. API Key não tem permissão de Copy Trading");
                    Log("   3. Endpoint não está disponível para sua conta\n");
                }

                // Teste 2: leadInfo
                Log("\n═══════════════════════════════════════════════════════");
                Log("👥 TESTE 2: GET /sapi/v1/copyTrading/futures/leadInfo");
                Log("   (Deve retornar os traders que você segue)");
                Log("═══════════════════════════════════════════════════════");
                
                var leadInfo = await api.GetLeadInfoAsync();
                
                if (leadInfo.Count > 0)
                {
                    Log($"✅ {leadInfo.Count} TRADERS SEGUIDOS!\n");
                    foreach (var lead in leadInfo)
                    {
                        Log($"👤 {lead.LeadTraderNickName}");
                        Log($"   Portfolio ID: {lead.PortfolioId}");
                        Log($"   Copy Mode: {lead.CopyMode}");
                        Log($"   Copy Ratio: {lead.CopyRatio}\n");
                    }
                }
                else
                {
                    Log("⚠️ NENHUM TRADER SEGUIDO ENCONTRADO\n");
                }

                // Teste 3: myCopyOrders
                Log("\n═══════════════════════════════════════════════════════");
                Log("📦 TESTE 3: GET /sapi/v1/copyTrading/futures/myCopyOrders");
                Log("   (Histórico de ordens copiadas)");
                Log("═══════════════════════════════════════════════════════");
                
                var orders = await api.GetMyCopyOrdersAsync(limit: 10);
                
                if (orders.Count > 0)
                {
                    Log($"✅ {orders.Count} ORDENS COPIADAS!\n");
                    foreach (var order in orders.Take(5))
                    {
                        Log($"📝 {order.Symbol} - {order.Side}");
                        Log($"   Trader: {order.LeadTraderNickName}");
                        Log($"   Quantidade: {order.OrigQty}");
                        Log($"   Preço: {order.Price}");
                        Log($"   Status: {order.Status}\n");
                    }
                }
                else
                {
                    Log("⚠️ NENHUMA ORDEM COPIADA ENCONTRADA\n");
                }

                // Teste 4: userStatus
                Log("\n═══════════════════════════════════════════════════════");
                Log("👤 TESTE 4: GET /sapi/v1/copyTrading/futures/userStatus");
                Log("   (Status se você for lead trader)");
                Log("═══════════════════════════════════════════════════════");
                
                var userStatus = await api.GetUserStatusAsync();
                Log($"Resposta: {userStatus}\n");

                // Teste 5: leadSymbol
                Log("\n═══════════════════════════════════════════════════════");
                Log("📋 TESTE 5: GET /sapi/v1/copyTrading/futures/leadSymbol");
                Log("═══════════════════════════════════════════════════════");
                
                var leadSymbol = await api.GetLeadSymbolAsync();
                Log($"Resposta: {leadSymbol}\n");

                Log("\n═══════════════════════════════════════════════════════");
                Log("✅ TESTES CONCLUÍDOS!");
                Log("═══════════════════════════════════════════════════════");

            }
            catch (Exception ex)
            {
                Log($"\n❌ ERRO: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
            }
        }

        private void Log(string message)
        {
            if (_outputBox.InvokeRequired)
            {
                _outputBox.Invoke(new Action(() => Log(message)));
            }
            else
            {
                _outputBox.AppendText(message + "\r\n");
                Console.WriteLine(message);
            }
        }
    }
}

