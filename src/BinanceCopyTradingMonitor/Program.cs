using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BinanceCopyTradingMonitor
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();
        
        [DllImport("kernel32.dll")]
        static extern bool FreeConsole();
        
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        
        private static bool _consoleVisible = true;
        
        public static void ToggleConsole()
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                _consoleVisible = !_consoleVisible;
                ShowWindow(handle, _consoleVisible ? SW_SHOW : SW_HIDE);
            }
        }
        
        public static void HideConsole()
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                _consoleVisible = false;
                ShowWindow(handle, SW_HIDE);
            }
        }
        
        public static void ShowConsole()
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                _consoleVisible = true;
                ShowWindow(handle, SW_SHOW);
            }
        }
        
        public static bool IsConsoleVisible => _consoleVisible;
        
        [STAThread]
        static void Main()
        {
            // Force console to always appear
            AllocConsole();
            
            Console.WriteLine("BINANCE COPY TRADING MONITOR - WEB SCRAPER MODE");
            Console.WriteLine($"Started: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            
            // Show public IP
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var ip = client.GetStringAsync("https://api.ipify.org").Result;
                Console.WriteLine($"Your public IP: {ip}");
            }
            catch { }
            
            Console.WriteLine();

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                Console.WriteLine("Starting Chromium scraper mode...\n");
                
                // Handler for Ctrl+C
                Console.CancelKeyPress += (s, e) =>
                {
                    Console.WriteLine("\nCtrl+C detected! Killing Chromium...");
                    KillAllChromium();
                    Environment.Exit(0);
                };
                
                // Handler for crash/unhandled exception
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Console.WriteLine("\nCRASH! Killing Chromium...");
                    KillAllChromium();
                };
                
                Application.Run(new CopyTradingScraperApp());
                
                try { Console.WriteLine("\nApplication closed normally"); } catch { }
                KillAllChromium();
            }
            catch (Exception ex)
            {
                try
                {
                    Console.WriteLine("\nFATAL ERROR");
                    Console.WriteLine($"Message: {ex.Message}");
                    Console.WriteLine($"Stack Trace:\n{ex.StackTrace}");
                }
                catch { }
                
                KillAllChromium();
                
                try
                {
                    MessageBox.Show(
                        $"FATAL ERROR:\n\n{ex.Message}\n\nSee console for details.",
                        "Fatal Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { }
            }
            finally
            {
                try { Console.WriteLine("Final cleanup: killing Chromium..."); } catch { }
                KillAllChromium();
            }
            
            try 
            { 
                Console.WriteLine("\nPress any key to close...");
                Console.ReadKey();
            } 
            catch { }
        }

        static void KillAllChromium()
        {
            try
            {
                try { Console.WriteLine("Killing all Chromium processes..."); } catch { }
                
                var processes = System.Diagnostics.Process.GetProcesses()
                    .Where(p => p.ProcessName.ToLower().Contains("chrom"))
                    .ToList();
                
                foreach (var process in processes)
                {
                    try
                    {
                        try { Console.WriteLine($"   {process.ProcessName} (PID: {process.Id})"); } catch { }
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch { }
                }
                
                try { Console.WriteLine("Chromium cleaned!"); } catch { }
            }
            catch { }
        }
    }
}
