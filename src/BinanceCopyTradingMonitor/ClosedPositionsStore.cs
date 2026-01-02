using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BinanceCopyTradingMonitor
{
    public class ClosedPositionsStore
    {
        private readonly string _folderPath;
        private List<ClosedPositionRecord> _currentWeekRecords = new();
        private readonly object _lock = new();
        
        public event Action<string>? OnLog;
        
        // Current week info
        private int _currentYear;
        private int _currentWeek;
        
        public ClosedPositionsStore()
        {
            _folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "closed_positions");
            
            // Ensure folder exists
            if (!Directory.Exists(_folderPath))
                Directory.CreateDirectory(_folderPath);
            
            // Set current week
            var now = DateTime.Now;
            _currentYear = now.Year;
            _currentWeek = GetWeekOfYear(now);
            
            // Load current week
            LoadCurrentWeek();
        }
        
        private static int GetWeekOfYear(DateTime date)
        {
            var culture = CultureInfo.CurrentCulture;
            return culture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }
        
        private string GetWeekFileName(int year, int week)
        {
            return Path.Combine(_folderPath, $"{year}-W{week:D2}.json");
        }
        
        private string GetCurrentWeekFileName()
        {
            return GetWeekFileName(_currentYear, _currentWeek);
        }
        
        private void CheckWeekRollover()
        {
            var now = DateTime.Now;
            var currentYear = now.Year;
            var currentWeek = GetWeekOfYear(now);
            
            if (currentYear != _currentYear || currentWeek != _currentWeek)
            {
                // Week changed - save current and switch
                Save();
                _currentYear = currentYear;
                _currentWeek = currentWeek;
                LoadCurrentWeek();
                Log($"[STORE] Rolled over to week {_currentYear}-W{_currentWeek:D2}");
            }
        }
        
        private void LoadCurrentWeek()
        {
            lock (_lock)
            {
                try
                {
                    var filePath = GetCurrentWeekFileName();
                    if (File.Exists(filePath))
                    {
                        var json = File.ReadAllText(filePath);
                        _currentWeekRecords = JsonConvert.DeserializeObject<List<ClosedPositionRecord>>(json) ?? new();
                        Log($"[STORE] Loaded {_currentWeekRecords.Count} records for week {_currentYear}-W{_currentWeek:D2}");
                    }
                    else
                    {
                        _currentWeekRecords = new();
                        Log($"[STORE] New week {_currentYear}-W{_currentWeek:D2}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[STORE] Error loading week: {ex.Message}");
                    _currentWeekRecords = new();
                }
            }
        }
        
        public List<ClosedPositionRecord> LoadWeek(int year, int week)
        {
            lock (_lock)
            {
                try
                {
                    var filePath = GetWeekFileName(year, week);
                    if (File.Exists(filePath))
                    {
                        var json = File.ReadAllText(filePath);
                        return JsonConvert.DeserializeObject<List<ClosedPositionRecord>>(json) ?? new();
                    }
                }
                catch (Exception ex)
                {
                    Log($"[STORE] Error loading week {year}-W{week:D2}: {ex.Message}");
                }
                return new();
            }
        }
        
        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    var filePath = GetCurrentWeekFileName();
                    var json = JsonConvert.SerializeObject(_currentWeekRecords, Formatting.Indented);
                    File.WriteAllText(filePath, json);
                }
                catch (Exception ex)
                {
                    Log($"[STORE] Error saving: {ex.Message}");
                }
            }
        }
        
        public void AddRecord(ClosedPositionRecord record)
        {
            lock (_lock)
            {
                CheckWeekRollover();
                _currentWeekRecords.Insert(0, record);  // Most recent first
                Save();
                Log($"[STORE] Added: {record.Symbol} @ {record.PnLPercent:+0.00;-0.00}% ({record.PnL:+0.00;-0.00} {record.Currency})");
            }
        }
        
        public void UpdatePnL(string id, decimal newPnL, string notes = "")
        {
            lock (_lock)
            {
                // Search in current week first
                var record = _currentWeekRecords.FirstOrDefault(r => r.Id == id);
                if (record != null)
                {
                    var oldPnL = record.PnL;
                    record.PnL = newPnL;
                    record.WasEdited = true;
                    if (!string.IsNullOrEmpty(notes))
                        record.Notes = notes;
                    Save();
                    Log($"[STORE] Updated {record.Symbol}: {oldPnL:+0.00;-0.00} -> {newPnL:+0.00;-0.00}");
                }
            }
        }
        
        public void DeleteRecord(string id)
        {
            lock (_lock)
            {
                var removed = _currentWeekRecords.RemoveAll(r => r.Id == id);
                if (removed > 0)
                {
                    Save();
                    Log($"[STORE] Deleted record {id}");
                }
            }
        }
        
        public List<ClosedPositionRecord> GetCurrentWeek()
        {
            lock (_lock)
            {
                CheckWeekRollover();
                return _currentWeekRecords.ToList();
            }
        }
        
        public List<ClosedPositionRecord> GetToday()
        {
            lock (_lock)
            {
                CheckWeekRollover();
                return _currentWeekRecords.Where(r => r.ClosedAt.Date == DateTime.Today).ToList();
            }
        }
        
        public ClosedPositionRecord? GetById(string id)
        {
            lock (_lock)
            {
                return _currentWeekRecords.FirstOrDefault(r => r.Id == id);
            }
        }
        
        // ====== AVAILABLE WEEKS ======
        
        public List<(int Year, int Week, string FileName)> GetAvailableWeeks()
        {
            var weeks = new List<(int Year, int Week, string FileName)>();
            
            try
            {
                if (Directory.Exists(_folderPath))
                {
                    var files = Directory.GetFiles(_folderPath, "*.json");
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        // Parse "2025-W51" format
                        if (fileName.Contains("-W"))
                        {
                            var parts = fileName.Split("-W");
                            if (parts.Length == 2 && 
                                int.TryParse(parts[0], out int year) && 
                                int.TryParse(parts[1], out int week))
                            {
                                weeks.Add((year, week, fileName));
                            }
                        }
                    }
                }
            }
            catch { }
            
            return weeks.OrderByDescending(w => w.Year).ThenByDescending(w => w.Week).ToList();
        }
        
        // ====== STATISTICS ======
        
        public decimal GetCurrentWeekPnL()
        {
            lock (_lock)
            {
                CheckWeekRollover();
                return _currentWeekRecords.Sum(r => r.PnL);
            }
        }
        
        public decimal GetTodayPnL()
        {
            lock (_lock)
            {
                CheckWeekRollover();
                return _currentWeekRecords.Where(r => r.ClosedAt.Date == DateTime.Today).Sum(r => r.PnL);
            }
        }
        
        public int GetCurrentWeekCount()
        {
            lock (_lock)
            {
                CheckWeekRollover();
                return _currentWeekRecords.Count;
            }
        }
        
        public int GetTodayCount()
        {
            lock (_lock)
            {
                CheckWeekRollover();
                return _currentWeekRecords.Count(r => r.ClosedAt.Date == DateTime.Today);
            }
        }
        
        public decimal GetAllTimePnL()
        {
            decimal total = 0;
            
            try
            {
                var weeks = GetAvailableWeeks();
                foreach (var (year, week, _) in weeks)
                {
                    var records = LoadWeek(year, week);
                    total += records.Sum(r => r.PnL);
                }
            }
            catch { }
            
            return total;
        }
        
        public int GetAllTimeCount()
        {
            int total = 0;
            
            try
            {
                var weeks = GetAvailableWeeks();
                foreach (var (year, week, _) in weeks)
                {
                    var records = LoadWeek(year, week);
                    total += records.Count;
                }
            }
            catch { }
            
            return total;
        }
        
        public string GetSummary()
        {
            var today = GetTodayPnL();
            var todayCount = GetTodayCount();
            var week = GetCurrentWeekPnL();
            var weekCount = GetCurrentWeekCount();
            var allTime = GetAllTimePnL();
            var allTimeCount = GetAllTimeCount();
            
            return $"Today: {today:+0.00;-0.00} USDT ({todayCount}) | Week: {week:+0.00;-0.00} USDT ({weekCount}) | All-time: {allTime:+0.00;-0.00} USDT ({allTimeCount})";
        }
        
        public string GetCurrentWeekName()
        {
            return $"{_currentYear}-W{_currentWeek:D2}";
        }
        
        private void Log(string message) => OnLog?.Invoke(message);
    }
}
