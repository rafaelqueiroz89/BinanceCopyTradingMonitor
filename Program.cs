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
                
                Console.WriteLine("\nApplication closed normally");
                KillAllChromium();
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nFATAL ERROR");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Stack Trace:\n{ex.StackTrace}");
                
                KillAllChromium();
                
                MessageBox.Show(
                    $"FATAL ERROR:\n\n{ex.Message}\n\nSee console for details.",
                    "Fatal Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Console.WriteLine("Final cleanup: killing Chromium...");
                KillAllChromium();
            }
            
            Console.WriteLine("\nPress any key to close...");
            Console.ReadKey();
        }

        static void KillAllChromium()
        {
            try
            {
                Console.WriteLine("Killing all Chromium processes...");
                
                var processes = System.Diagnostics.Process.GetProcesses()
                    .Where(p => p.ProcessName.ToLower().Contains("chrom"))
                    .ToList();
                
                foreach (var process in processes)
                {
                    try
                    {
                        Console.WriteLine($"   {process.ProcessName} (PID: {process.Id})");
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch { }
                }
                
                Console.WriteLine("Chromium cleaned!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
