using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using AngleSharp;
using AngleSharp.Html.Parser;

namespace BinanceCopyTradingMonitor
{
    public class BinanceScraperManager : IDisposable
    {
        private IBrowser? _browser;
        private IPage? _page;
        private bool _isRunning;
        
        private ConcurrentDictionary<string, IPage> _traderPages = new();

        public event Action<List<ScrapedPosition>>? OnPositionsUpdated;
        public event Action<string>? OnError;
        public event Action<string>? OnLog;
        public event Action? OnRestartRequested;
        public event Action<string>? OnGrowthScraped;
        public event Action<string>? OnGrowthScrapedOnDemand;

        private volatile bool _refreshRequested = false;
        private volatile bool _restartRequested = false;
        private DateTime _lastAutoRefresh = DateTime.Now;
        private const int AUTO_REFRESH_MINUTES = 10;

        public void RequestRefresh()
        {
            _refreshRequested = true;
            Log("Refresh requested - will reload pages on next cycle");
        }
        
        public void RequestRestart()
        {
            _restartRequested = true;
            Log("Restart requested - will kill Chrome and restart");
        }
        
        public async Task<bool> CloseModalAsync(string traderName)
        {
            try
            {
                Log($"[MODAL] Closing modal for {traderName}");
                
                if (!_traderPages.TryGetValue(traderName, out var page) || page == null || page.IsClosed)
                {
                    Error($"[MODAL] Page not found for trader: {traderName}");
                    return false;
                }
                
                // Click the X button to close the modal
                var clicked = await page.EvaluateFunctionAsync<bool>(@"() => {
                    // Find the modal close button (X icon)
                    const closeBtn = document.querySelector('.bn-modal-header-next[role=""button""][aria-label=""Close""]');
                    if (closeBtn) {
                        closeBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                        return true;
                    }
                    // Alternative: find any close button with the X svg
                    const closeSvg = document.querySelector('.bn-modal-header-next svg');
                    if (closeSvg) {
                        closeSvg.parentElement.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                        return true;
                    }
                    return false;
                }");
                
                if (clicked)
                {
                    Log($"[MODAL] Successfully closed modal");
                    return true;
                }
                else
                {
                    Error($"[MODAL] Could not find close button");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Error($"[MODAL] Error closing modal: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> ClickClosePositionAsync(string traderName, string symbol, string size)
        {
            try
            {
                Log($"[CLOSE] Clicking Close Position for {traderName} - {symbol} (size: {size})");
                
                if (!_traderPages.TryGetValue(traderName, out var page) || page == null || page.IsClosed)
                {
                    Error($"[CLOSE] Page not found for trader: {traderName}");
                    return false;
                }
                
                // Bring the page to front
                await page.BringToFrontAsync();
                
                // Find the row with the symbol AND size, then click Close Position in column 10
                var clicked = await page.EvaluateFunctionAsync<bool>(@"(targetSymbol, targetSize) => {
                    const rows = document.querySelectorAll('tbody.bn-web-table-tbody tr[role=""row""]');
                    
                    for (const row of rows) {
                        const col1 = row.querySelector('td[aria-colindex=""1""]');
                        const col2 = row.querySelector('td[aria-colindex=""2""]');
                        if (!col1 || !col2) continue;
                        
                        const symbolText = col1.innerText || col1.textContent || '';
                        const sizeText = col2.innerText || col2.textContent || '';
                        
                        // Match by symbol AND size to handle multiple positions of same coin
                        if (symbolText.includes(targetSymbol) && sizeText.includes(targetSize)) {
                            // Close Position is in column 10
                            const closeCell = row.querySelector('td[aria-colindex=""10""]');
                            if (closeCell) {
                                const closeLink = closeCell.querySelector('span.cursor-pointer');
                                if (closeLink) {
                                    closeLink.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
                }", symbol, size);
                
                if (clicked)
                {
                    Log($"[CLOSE] Successfully clicked Close Position for {symbol}");
                    return true;
                }
                else
                {
                    Error($"[CLOSE] Could not find Close Position for {symbol} in {traderName}'s positions");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Error($"[CLOSE] Error clicking Close Position: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> ConfirmClosePositionAsync(string traderName)
        {
            try
            {
                Log($"[CLOSE] Confirming close position for {traderName}");
                
                if (!_traderPages.TryGetValue(traderName, out var page) || page == null || page.IsClosed)
                {
                    Error($"[CLOSE] Page not found for trader: {traderName}");
                    return false;
                }
                
                // Wait a bit for modal to fully appear
                await Task.Delay(500);
                
                // Click the Confirm button in the close position modal
                var confirmed = await page.EvaluateFunctionAsync<bool>(@"() => {
                    // Look for the confirm button in the modal (usually yellow/primary button with 'Confirm' text)
                    const buttons = document.querySelectorAll('.bn-modal button, .bn-dialog button, [class*=""modal""] button');
                    for (const btn of buttons) {
                        const text = (btn.innerText || btn.textContent || '').toLowerCase();
                        if (text.includes('confirm') || text === 'close') {
                            btn.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                            return true;
                        }
                    }
                    // Alternative: look for primary/yellow button in modal
                    const primaryBtn = document.querySelector('.bn-modal .bn-button--primary, .bn-modal [class*=""yellow""], .bn-modal button[class*=""primary""]');
                    if (primaryBtn) {
                        primaryBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                        return true;
                    }
                    return false;
                }");
                
                if (confirmed)
                {
                    Log($"[CLOSE] Successfully confirmed close position");
                    return true;
                }
                else
                {
                    Error($"[CLOSE] Could not find confirm button in modal");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Error($"[CLOSE] Error confirming close: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> ClickTPSLButtonAsync(string traderName, string symbol, string size)
        {
            try
            {
                Log($"[TP/SL] Clicking TP/SL button for {traderName} - {symbol} (size: {size})");
                
                if (!_traderPages.TryGetValue(traderName, out var page) || page == null || page.IsClosed)
                {
                    Error($"[TP/SL] Page not found for trader: {traderName}");
                    return false;
                }
                
                // Bring the page to front
                await page.BringToFrontAsync();
                
                // Find the row with the symbol AND size, then click the TP/SL pencil icon in column 9
                var clicked = await page.EvaluateFunctionAsync<bool>(@"(targetSymbol, targetSize) => {
                    // Find all data rows (skip measure row)
                    const rows = document.querySelectorAll('tbody.bn-web-table-tbody tr[role=""row""]');
                    
                    for (const row of rows) {
                        // Get symbol from column 1, size from column 2
                        const col1 = row.querySelector('td[aria-colindex=""1""]');
                        const col2 = row.querySelector('td[aria-colindex=""2""]');
                        if (!col1 || !col2) continue;
                        
                        const symbolText = col1.innerText || col1.textContent || '';
                        const sizeText = col2.innerText || col2.textContent || '';
                        
                        // Match by symbol AND size to handle multiple positions of same coin
                        if (symbolText.includes(targetSymbol) && sizeText.includes(targetSize)) {
                            // TP/SL pencil icon is in column 9
                            const tpslCell = row.querySelector('td[aria-colindex=""9""]');
                            if (tpslCell) {
                                const pencilIcon = tpslCell.querySelector('svg[viewBox=""0 0 24 24""]');
                                if (pencilIcon) {
                                    // Use dispatchEvent for SVG elements
                                    pencilIcon.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
                }", symbol, size);
                
                if (clicked)
                {
                    Log($"[TP/SL] Successfully clicked TP/SL button for {symbol}");
                    return true;
                }
                else
                {
                    Error($"[TP/SL] Could not find TP/SL button for {symbol} in {traderName}'s positions");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Error($"[TP/SL] Error clicking TP/SL: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StartAsync()
        {
            try
            {
                Log("Starting Web Scraper (PuppeteerSharp)");

                KillAllChromiumProcesses();
                CleanupScreenshots();

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

                Log("Navigating to Binance Copy Trading page...");
                await _page.GoToAsync("https://www.binance.com/en/copy-trading",
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });

                // Scrape the growth value from the main page
                var growthValue = await ScrapeGrowthValueAsync();
                Log($"Growth value scraped: {growthValue}");

                // Navigate to copy management for trader positions
                Log("Navigating to Binance Copy Management...");
                await _page.GoToAsync("https://www.binance.com/en/copy-trading/copy-management",
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });

                Log("Waiting for login/traders to appear...");
                bool tradersFound = false;
                int waitAttempts = 0;
                const int maxWaitMinutes = 5;
                
                while (!tradersFound && waitAttempts < maxWaitMinutes * 12)
                {
                    try
                    {
                        var hasTraders = await _page.EvaluateExpressionAsync<bool>(
                            "document.querySelectorAll('.t-subtitle4.text-PrimaryText.cursor-pointer').length > 0"
                        );
                        
                        if (hasTraders)
                        {
                            tradersFound = true;
                            Log("Traders detected! Continuing automatically...");
                        }
                        else
                        {
                            waitAttempts++;
                            if (waitAttempts % 6 == 0)
                            {
                                Log($"Still waiting for login... ({waitAttempts * 5 / 60}m {(waitAttempts * 5) % 60}s)");
                            }
                            await Task.Delay(5000);
                        }
                    }
                    catch
                    {
                        await Task.Delay(5000);
                        waitAttempts++;
                    }
                }
                
                if (!tradersFound)
                {
                    Error($"No traders found after {maxWaitMinutes} minutes. Please login to Binance manually.");
                    return false;
                }
                
                await Task.Delay(2000);

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
                    
                    if (_restartRequested)
                    {
                        _restartRequested = false;
                        OnRestartRequested?.Invoke();
                        return;
                    }
                    
                    if (_refreshRequested)
                    {
                        _refreshRequested = false;
                        await RefreshAllPagesAsync();
                        _lastAutoRefresh = DateTime.Now;
                    }
                    
                    if ((DateTime.Now - _lastAutoRefresh).TotalMinutes >= AUTO_REFRESH_MINUTES)
                    {
                        Log($"Auto-refresh triggered (every {AUTO_REFRESH_MINUTES} minutes)");
                        await RefreshAllPagesAsync();
                        _lastAutoRefresh = DateTime.Now;
                    }
                    
                    var positions = await ExtractPositionsAsync();
                    
                    if (positions.Count > 0)
                    {
                        OnPositionsUpdated?.Invoke(positions);
                    }

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

        private async Task RefreshAllPagesAsync()
        {
            try
            {
                Log("Refreshing all trader pages in parallel...");
                
                var refreshTasks = _traderPages.ToList().Select(kvp => RefreshSinglePageAsync(kvp.Key, kvp.Value));
                await Task.WhenAll(refreshTasks);
                
                Log("All pages refreshed!");
            }
            catch (Exception ex)
            {
                Error($"Error in RefreshAllPagesAsync: {ex.Message}");
            }
        }

        private async Task RefreshSinglePageAsync(string traderName, IPage page)
        {
            try
            {
                if (page == null || page.IsClosed) return;
                
                await page.ReloadAsync(new NavigationOptions 
                { 
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                    Timeout = 30000 
                });
                
                bool clicked = false;
                for (int attempt = 1; attempt <= 3 && !clicked; attempt++)
                {
                    await Task.Delay(1500 * attempt); // 1.5s, 3s, 4.5s
                    
                    clicked = await page.EvaluateFunctionAsync<bool>(@"(targetName) => {
                        const main = document.querySelector('.copy-mgmt-wrap');
                        if (!main) return false;
                        const traderBlocks = Array.from(main.querySelectorAll('.bn-flex.py-\\[24px\\].flex-col.gap-\\[24px\\]'));
                        for (const block of traderBlocks) {
                            const nameEl = block.querySelector('.t-subtitle4.text-PrimaryText.cursor-pointer');
                            if (!nameEl) continue;
                            if (nameEl.textContent.trim() === targetName) {
                                const expandBtn = block.querySelector('.bn-flex.gap-\\[4px\\].items-center.cursor-pointer');
                                if (expandBtn) { expandBtn.click(); return true; }
                            }
                        }
                        return false;
                    }", traderName);
                }
                
                if (clicked)
                {
                    try { await page.WaitForSelectorAsync("table", new WaitForSelectorOptions { Timeout = 10000 }); } catch { }
                    Log($"Refreshed: {traderName}");
                }
                else
                {
                    Error($"Refreshed {traderName} but failed to expand after 3 attempts");
                }
            }
            catch (Exception ex)
            {
                Error($"Error refreshing {traderName}: {ex.Message}");
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
                
                var hasElement = await _page.EvaluateExpressionAsync<bool>("!!document.querySelector('.copy-mgmt-wrap')");
                
                if (!hasElement)
                {
                    Error("Element .copy-mgmt-wrap not found!");
                    return new List<ScrapedTraderData>();
                }

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

            Log($"Opening {traders.Count} trader tabs in PARALLEL...");
            
            var tasks = traders.Select(trader => OpenSingleTraderTabAsync(trader));
            await Task.WhenAll(tasks);
            
            Log($"All {_traderPages.Count} trader tabs ready!");
        }
        
        private async Task OpenSingleTraderTabAsync(ScrapedTraderData trader)
        {
            try
            {
                Log($"Opening tab for {trader.Name}...");

                var page = await _browser!.NewPageAsync();
                await page.GoToAsync("https://www.binance.com/en/copy-trading/copy-management", 
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 60000 });

                await Task.Delay(2000);

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
                    return;
                }

                try
                {
                    await page.WaitForSelectorAsync("table", new WaitForSelectorOptions { Timeout = 10000 });
                    
                    var rowCount = await page.EvaluateExpressionAsync<int>(
                        "document.querySelectorAll('tbody.bn-web-table-tbody tr:not(.bn-web-table-measure-row)').length"
                    );
                    Log($"{trader.Name}: {rowCount} rows - READY!");
                    
                    _traderPages[trader.Name] = page;
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
                    .GroupBy(p => $"{p.Trader}|{p.Symbol}|{p.Side}|{p.Size}")
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

                    var symbolEl = cells[0].QuerySelector(".t-caption2");
                    if (symbolEl == null)
                    {
                        continue;
                    }
                    var symbol = symbolEl.TextContent.Trim();

                    var sizeEl = cells[1].QuerySelector(".t-body3");
                    var size = sizeEl?.TextContent.Trim() ?? "";

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
            if (OnLog != null)
                try { OnLog.Invoke(message); } catch { }
            else
                try { Console.WriteLine(message); } catch { }
        }

        private void Error(string message)
        {
            if (OnError != null)
                try { OnError.Invoke(message); } catch { }
            else
                try { Console.WriteLine($"ERROR: {message}"); } catch { }
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

        private async Task<string> ScrapeGrowthValueAsync()
        {
            try
            {
                if (_page == null)
                {
                    Error("Page is null when trying to scrape growth value");
                    return "ERROR";
                }

                // Wait for the selector to appear
                await _page.WaitForSelectorAsync("div.typography-headline0.md\\:typography-headline2", new WaitForSelectorOptions { Timeout = 10000 });

                // Extract the text content from the selector
                var growthValue = await _page.EvaluateExpressionAsync<string>(
                    "document.querySelector('div.typography-headline0.md\\\\:typography-headline2')?.textContent?.trim() || 'NOT_FOUND'"
                );

                if (growthValue == "NOT_FOUND")
                {
                    Error("Growth value selector not found");
                    return "NOT_FOUND";
                }

                // Fire the event to notify listeners
                OnGrowthScraped?.Invoke(growthValue);

                return growthValue;
            }
            catch (Exception ex)
            {
                Error($"Error scraping growth value: {ex.Message}");
                return "ERROR";
            }
        }

        public async Task<string> ScrapeGrowthOnDemandAsync()
        {
            try
            {
                if (_browser == null)
                {
                    Error("Browser not initialized for on-demand growth scraping");
                    return "ERROR";
                }

                // Create a new page for scraping growth
                var growthPage = await _browser.NewPageAsync();

                try
                {
                    Log("Navigating to Binance Copy Trading for growth scraping...");
                    await growthPage.GoToAsync("https://www.binance.com/en/copy-trading",
                        new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 30000 });

                    // Scrape the growth value
                    var growthValue = await ScrapeGrowthValueFromPageAsync(growthPage);

                    Log($"On-demand growth scraping completed: {growthValue}");
                    OnGrowthScrapedOnDemand?.Invoke(growthValue);

                    return growthValue;
                }
                finally
                {
                    await growthPage.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Error($"Error in on-demand growth scraping: {ex.Message}");
                return "ERROR";
            }
        }

        private async Task<string> ScrapeGrowthValueFromPageAsync(IPage page)
        {
            try
            {
                // Wait for the selector to appear
                await page.WaitForSelectorAsync("div.typography-headline0.md\\:typography-headline2", new WaitForSelectorOptions { Timeout = 10000 });

                // Extract the text content from the selector
                var growthValue = await page.EvaluateExpressionAsync<string>(
                    "document.querySelector('div.typography-headline0.md\\\\:typography-headline2')?.textContent?.trim() || 'NOT_FOUND'"
                );

                return growthValue == "NOT_FOUND" ? "NOT_FOUND" : growthValue;
            }
            catch (Exception ex)
            {
                Error($"Error scraping growth from page: {ex.Message}");
                return "ERROR";
            }
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
                // Extract numbers using regex - simpler and more robust
                var numbers = System.Text.RegularExpressions.Regex.Matches(rawPnL, @"-?[\d,\.]+");
                
                if (numbers.Count >= 1)
                {
                    // First number is PnL value - remove commas (thousands sep) and normalize
                    var pnlStr = numbers[0].Value.Replace(",", "");
                    if (decimal.TryParse(pnlStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pnlValue))
                    {
                        PnL = pnlValue;
                    }
                }
                
                if (numbers.Count >= 2)
                {
                    // Second number is percentage
                    var percentStr = numbers[1].Value.Replace(",", ".");
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
