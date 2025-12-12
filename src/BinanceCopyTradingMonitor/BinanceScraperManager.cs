using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using AngleSharp;
using AngleSharp.Html.Parser;

namespace BinanceCopyTradingMonitor
{
    /// <summary>
    /// Web Scraper for Binance Copy Trading using PuppeteerSharp
    /// Reads directly from the Binance UI
    /// </summary>
    public class BinanceScraperManager : IDisposable
    {
        private IBrowser? _browser;
        private IPage? _page;
        private bool _isRunning;
        
        // One tab (Page) for each trader
        private Dictionary<string, IPage> _traderPages = new Dictionary<string, IPage>();

        public event Action<List<ScrapedPosition>>? OnPositionsUpdated;
        public event Action<string>? OnError;
        public event Action<string>? OnLog;

        public async Task<bool> StartAsync()
        {
            try
            {
                Log("Starting Web Scraper (PuppeteerSharp)");

                // Kill Chromium before starting
                KillAllChromiumProcesses();

                // Clean old screenshots
                CleanupScreenshots();

                // Download Chromium if necessary
                var browserFetcher = new BrowserFetcher();
                var installedBrowser = browserFetcher.GetInstalledBrowsers().FirstOrDefault();
                
                if (installedBrowser == null)
                {
                    Log("Chromium not found. Downloading... (this may take a few minutes)");
                    await browserFetcher.DownloadAsync();
                    Log("Chromium download complete!");
                }
                else
                {
                    Log("Chromium found locally");
                }

                // Open browser
                // PHASE 1: Visible browser for login
                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = false,
                    Args = new[] { 
                        "--start-maximized",
                        "--disable-blink-features=AutomationControlled"
                    },
                    DefaultViewport = null,
                    UserDataDir = "./chrome-profile"
                });

                _page = (await _browser.PagesAsync()).First();

                Log("Navigating to Binance Copy Management...");
                await _page.GoToAsync("https://www.binance.com/en/copy-trading/copy-management", 
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });

                Log("\nACTION REQUIRED:");
                Log("1. Login to Binance (if necessary)");
                Log("2. Navigate to your Copy Trading positions");
                Log("3. Press ENTER here in the console\n");

                Console.WriteLine("Press ENTER to continue...");
                Console.ReadLine();

                // PHASE 2: Close visible browser
                await _browser.CloseAsync();
                await Task.Delay(1000);
                
                // PHASE 3: Reopen in HEADLESS mode with performance flags
                Log("Opening browser in BACKGROUND (headless + performance)...");
                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    Args = new[] { 
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-gpu",
                        "--disable-dev-shm-usage",
                        "--disable-software-rasterizer",
                        "--disable-extensions",
                        "--disable-background-timer-throttling",
                        "--disable-backgrounding-occluded-windows",
                        "--disable-renderer-backgrounding",
                        "--disable-blink-features=AutomationControlled",
                        "--mute-audio"
                    },
                    DefaultViewport = null,
                    UserDataDir = "./chrome-profile"
                });
                
                _page = await _browser.NewPageAsync();
                await _page.GoToAsync("https://www.binance.com/en/copy-trading/copy-management", 
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });
                
                Log("Browser in BACKGROUND - Login maintained\n");

                // Wait for page to load completely
                await Task.Delay(3000);
                
                // Wait specifically for trader names
                await _page.WaitForSelectorAsync(".t-subtitle4.text-PrimaryText", new WaitForSelectorOptions { Timeout = 15000 });
                await Task.Delay(2000);

                // Identify traders on page
                var traders = await IdentifyTradersAsync();
                
                if (traders.Count == 0)
                {
                    Error("No traders found!");
                    Log("Trying to reload page...");
                    await _page.ReloadAsync(new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                    await Task.Delay(3000);
                    traders = await IdentifyTradersAsync();
                    
                    if (traders.Count == 0)
                    {
                        Error("Still no traders. Verify you are on the correct page!");
                        return false;
                    }
                }

                Log($"{traders.Count} traders found: {string.Join(", ", traders.Select(t => t.Name))}");

                // Open 1 tab for each trader
                Log("Opening 1 tab for each trader...");
                await OpenBrowsersForEachTraderAsync(traders);

                Log("Removing header, footer and unnecessary grid...");
                await _page.EvaluateFunctionAsync(@"() => {
                    document.querySelectorAll('header').forEach(h => h.remove());
                    document.querySelectorAll('footer').forEach(f => f.remove());
                    
                    const badGrids = document.querySelectorAll('.bn-flex.grid.grid-cols-1');
                    badGrids.forEach(g => {
                        if (g.className.includes('md:grid-cols-2') && g.className.includes('lg:grid-cols-4')) {
                            g.remove();
                        }
                    });
                }");

                Log("Starting real-time monitoring...\n");

                _isRunning = true;
                _ = Task.Run(MonitorLoop);

                return true;
            }
            catch (Exception ex)
            {
                Error($"Error starting scraper: {ex.Message}");
                return false;
            }
        }

        private int _cycleCount = 0;
        
        private async Task MonitorLoop()
        {
            while (_isRunning && _page != null)
            {
                try
                {
                    _cycleCount++;
                    
                    var positions = await ExtractPositionsAsync();
                    
                    if (positions.Count > 0)
                    {
                        OnPositionsUpdated?.Invoke(positions);
                    }

                    // Empty recycle bin every 20 cycles (~20 seconds)
                    if (_cycleCount % 20 == 0)
                    {
                        EmptyRecycleBin();
                    }

                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Error($"Error in loop: {ex.Message}");
                    await Task.Delay(10000);
                }
            }
        }

        private async Task<List<ScrapedTraderData>> IdentifyTradersAsync()
        {
            try
            {
                if (_page == null)
                {
                    Error("_page is null!");
                    return new List<ScrapedTraderData>();
                }
                
                // Verify element exists
                var hasElement = await _page.EvaluateExpressionAsync<bool>("!!document.querySelector('.copy-mgmt-wrap')");
                
                if (!hasElement)
                {
                    Error("Element .copy-mgmt-wrap not found!");
                    return new List<ScrapedTraderData>();
                }

                // Direct extraction - always works!
                var names = await _page.EvaluateExpressionAsync<string[]>(
                    @"Array.from(document.querySelectorAll('.t-subtitle4.text-PrimaryText.cursor-pointer')).map(el => el.textContent.trim())"
                );
                
                var traders = names.Select((name, index) => new ScrapedTraderData
                {
                    Name = name,
                    Index = index,
                    HasExpandButton = true
                }).ToList();

                return traders;
            }
            catch (Exception ex)
            {
                Error($"Error identifying traders: {ex.Message}");
                return new List<ScrapedTraderData>();
            }
        }

        private async Task OpenBrowsersForEachTraderAsync(List<ScrapedTraderData> traders)
        {
            if (_browser == null)
            {
                Error("Browser not initialized!");
                return;
            }

            foreach (var trader in traders)
            {
                try
                {
                    Log($"Opening tab for {trader.Name}...");

                    // Open new tab in the same browser
                    var page = await _browser.NewPageAsync();
                    await page.GoToAsync("https://www.binance.com/en/copy-trading/copy-management", 
                        new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });

                    await Task.Delay(2000);

                    // Click "Expand Details" for this trader by NAME (not index!)
                    var clicked = await page.EvaluateFunctionAsync<bool>(@"(targetName) => {
                        const main = document.querySelector('.copy-mgmt-wrap');
                        if (!main) return false;
                        
                        const traderBlocks = Array.from(main.querySelectorAll('.bn-flex.py-\\[24px\\].flex-col.gap-\\[24px\\]'));
                        
                        for (const block of traderBlocks) {
                            const nameEl = block.querySelector('.t-subtitle4.text-PrimaryText.cursor-pointer');
                            if (!nameEl) continue;
                            
                            const traderName = nameEl.textContent.trim();
                            
                            if (traderName === targetName) {
                                const expandBtn = block.querySelector('.bn-flex.gap-\\[4px\\].items-center.cursor-pointer');
                                if (expandBtn) {
                                    expandBtn.click();
                                    return true;
                                }
                            }
                        }
                        
                        return false;
                    }", trader.Name);
                    
                    if (!clicked)
                    {
                        Error($"Failed to click Expand Details for {trader.Name}!");
                        continue;
                    }

                    // Wait for table to appear (critical for background tabs!)
                    try
                    {
                        await page.WaitForSelectorAsync("table", new WaitForSelectorOptions { Timeout = 10000 });
                        
                        // Verify rows exist
                        var rowCount = await page.EvaluateExpressionAsync<int>(
                            "document.querySelectorAll('tbody.bn-web-table-tbody tr:not(.bn-web-table-measure-row)').length"
                        );
                        Log($"{trader.Name}: {rowCount} rows in table");
                        
                        // Save tab for this trader
                        _traderPages[trader.Name] = page;
                        Log($"{trader.Name} ready! Table always expanded!");
                    }
                    catch (Exception ex)
                    {
                        Error($"Timeout waiting for table for {trader.Name}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Error($"Error opening tab for {trader.Name}: {ex.Message}");
                }
            }
        }

        private async Task<List<ScrapedPosition>> ExtractPositionsAsync()
        {
            if (!_isRunning) return new List<ScrapedPosition>();
            
            try
            {
                var allPositions = new List<ScrapedPosition>();
                var pagesToExtract = _traderPages.ToList();

                var tasks = pagesToExtract.Select(async kvp =>
                {
                    if (!_isRunning) return new List<ScrapedPosition>();
                    
                    var traderName = kvp.Key;
                    var page = kvp.Value;

                    try
                    {
                        if (!_isRunning || page == null) return new List<ScrapedPosition>();
                        
                        var hasTable = await page.EvaluateExpressionAsync<bool>("!!document.querySelector('table')");
                        if (!hasTable || !_isRunning)
                        {
                            return new List<ScrapedPosition>();
                        }

                        var tableHtml = await page.EvaluateExpressionAsync<string>(
                            "document.querySelector('table')?.outerHTML || ''"
                        );

                        if (string.IsNullOrEmpty(tableHtml) || !_isRunning)
                        {
                            return new List<ScrapedPosition>();
                        }

                        var positions = ParseTableHtml(tableHtml, traderName);
                        
                        return positions;
                    }
                    catch
                    {
                        return new List<ScrapedPosition>();
                    }
                }).ToList();

                var results = await Task.WhenAll(tasks);
                
                if (!_isRunning) return new List<ScrapedPosition>();
                
                foreach (var positions in results)
                {
                    allPositions.AddRange(positions);
                }

                var uniquePositions = allPositions
                    .GroupBy(p => $"{p.Trader}|{p.Symbol}")
                    .Select(g => g.First())
                    .ToList();
                
                return uniquePositions;
            }
            catch
            {
                return new List<ScrapedPosition>();
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _traderPages.Clear();
            Thread.Sleep(500);
            KillAllChromiumProcesses();
        }

        private void KillAllChromiumProcesses()
        {
            try
            {
                Log("Killing all Chromium processes...");
                
                var chromiumProcesses = System.Diagnostics.Process.GetProcesses()
                    .Where(p => p.ProcessName.ToLower().Contains("chrom"))
                    .ToList();
                
                foreach (var process in chromiumProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error killing {process.ProcessName}: {ex.Message}");
                    }
                }
                
                Log("All Chromium processes terminated!");
            }
            catch (Exception ex)
            {
                Log($"Error killing Chromium processes: {ex.Message}");
            }
        }

        private List<ScrapedPosition> ParseTableHtml(string html, string traderName)
        {
            var positions = new List<ScrapedPosition>();

            try
            {
                var parser = new HtmlParser();
                var document = parser.ParseDocument(html);

                // Find all rows in tbody, ignoring measure-row
                var rows = document.QuerySelectorAll("tbody.bn-web-table-tbody tr")
                    .Where(row => !row.ClassList.Contains("bn-web-table-measure-row"))
                    .ToList();

                foreach (var row in rows)
                {
                    var cells = row.QuerySelectorAll("td").ToList();
                    
                    if (cells.Count < 8)
                    {
                        continue;
                    }

                    // Symbol: cell 0, inside .t-caption2
                    var symbolEl = cells[0].QuerySelector(".t-caption2");
                    if (symbolEl == null)
                    {
                        continue;
                    }
                    var symbol = symbolEl.TextContent.Trim();

                    // Size: cell 1, inside .t-body3
                    var sizeEl = cells[1].QuerySelector(".t-body3");
                    var size = sizeEl?.TextContent.Trim() ?? "";

                    // Margin: cell 2, inside .t-body3
                    var marginEl = cells[2].QuerySelector(".t-body3");
                    var margin = marginEl?.TextContent.Trim() ?? "";

                    // PNL: cell 7, get all text
                    var pnlRaw = cells[7].TextContent.Trim();

                    var position = new ScrapedPosition
                    {
                        Trader = traderName,
                        Symbol = symbol,
                        Side = "",
                        Size = size,
                        Margin = margin
                    };
                    position.ParsePnL(pnlRaw);
                    positions.Add(position);
                }
            }
            catch (Exception ex)
            {
                Error($"Error parsing HTML: {ex.Message}");
            }

            return positions;
        }

        private void Log(string message)
        {
            try { Console.WriteLine(message); } catch { }
            try { OnLog?.Invoke(message); } catch { }
        }

        private void Error(string message)
        {
            try { Console.WriteLine($"ERROR: {message}"); } catch { }
            try { OnError?.Invoke(message); } catch { }
        }

        private void CleanupScreenshots()
        {
            try
            {
                var screenshotsDir = "./screenshots";
                if (System.IO.Directory.Exists(screenshotsDir))
                {
                    var files = System.IO.Directory.GetFiles(screenshotsDir, "*.png");
                    foreach (var file in files)
                    {
                        try
                        {
                            System.IO.File.Delete(file);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void EmptyRecycleBin()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c rd /s /q %systemdrive%\\$Recycle.Bin",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                
                var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit(2000);
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                _isRunning = false;
                
                foreach (var kvp in _traderPages)
                {
                    try
                    {
                        kvp.Value?.CloseAsync().Wait(500);
                    }
                    catch { }
                }
                _traderPages.Clear();
                
                try { _page?.CloseAsync().Wait(500); } catch { }
                try { _browser?.CloseAsync().Wait(500); } catch { }
                try { _browser?.Dispose(); } catch { }
            }
            catch { }
        }
    }

    public class ScrapedPosition
    {
        public string Trader { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Side { get; set; } = "";
        public string Size { get; set; } = "";
        public string Margin { get; set; } = "";
        public string PnLRaw { get; set; } = "";
        public decimal PnL { get; set; } = 0;
        public string PnLCurrency { get; set; } = "USDT";
        public decimal PnLPercentage { get; set; } = 0;
        
        public void ParsePnL(string rawPnL)
        {
            PnLRaw = rawPnL;
            if (string.IsNullOrEmpty(rawPnL)) return;
            
            try
            {
                // Format: "-1.10 USDT-4.80%" or "+0.13 USDT+0.15%"
                var text = rawPnL.Replace(",", ".").Trim();
                
                // Find currency position (USDT, USDC, etc)
                var currencyIndex = text.IndexOf("USDT", StringComparison.OrdinalIgnoreCase);
                if (currencyIndex == -1) currencyIndex = text.IndexOf("USDC", StringComparison.OrdinalIgnoreCase);
                if (currencyIndex == -1) return;
                
                // Extract PnL value (before currency)
                var pnlStr = text.Substring(0, currencyIndex).Trim();
                if (decimal.TryParse(pnlStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pnlValue))
                {
                    PnL = pnlValue;
                }
                
                // Extract currency
                PnLCurrency = text.Substring(currencyIndex, 4);
                
                // Extract percentage (after currency)
                var afterCurrency = text.Substring(currencyIndex + 4);
                var percentIndex = afterCurrency.IndexOf('%');
                if (percentIndex > 0)
                {
                    var percentStr = afterCurrency.Substring(0, percentIndex).Trim();
                    if (decimal.TryParse(percentStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var percentValue))
                    {
                        PnLPercentage = percentValue;
                    }
                }
            }
            catch { }
        }
    }

    public class ScrapedTraderData
    {
        public string Name { get; set; } = "";
        public int Index { get; set; } = 0;
        public bool HasExpandButton { get; set; } = false;
    }

    public class PositionRow
    {
        public string Symbol { get; set; } = "";
        public string Size { get; set; } = "";
        public string Margin { get; set; } = "";
        public string EntryPrice { get; set; } = "";
        public string MarkPrice { get; set; } = "";
        public string PnL { get; set; } = "";
        public string RawData { get; set; } = "";
    }

    public class ScraperResult
    {
        public int TraderCount { get; set; }
        public List<TraderInfo> Traders { get; set; } = new List<TraderInfo>();
    }

    public class TraderInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public bool HasExpandButton { get; set; }
    }
}
