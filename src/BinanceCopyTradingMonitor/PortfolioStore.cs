using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BinanceCopyTradingMonitor
{
    public class PortfolioStore
    {
        private readonly string _filePath;
        private PortfolioData _portfolio = new();
        private readonly object _lock = new();
        
        public event Action<string>? OnLog;
        
        public PortfolioStore()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portfolio.json");
            Load();
        }
        
        public void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        var json = File.ReadAllText(_filePath);
                        _portfolio = JsonConvert.DeserializeObject<PortfolioData>(json) ?? new PortfolioData();
                        Log($"[PORTFOLIO] Loaded portfolio data (Initial: {_portfolio.InitialValue}, Current: {_portfolio.CurrentValue})");
                    }
                    else
                    {
                        _portfolio = new PortfolioData();
                        Log($"[PORTFOLIO] Created new portfolio data");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PORTFOLIO] Error loading: {ex.Message}");
                    _portfolio = new PortfolioData();
                }
            }
        }
        
        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_portfolio, Formatting.Indented);
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    Log($"[PORTFOLIO] Error saving: {ex.Message}");
                }
            }
        }
        
        public PortfolioData GetPortfolio()
        {
            lock (_lock)
            {
                return new PortfolioData
                {
                    InitialValue = _portfolio.InitialValue,
                    InitialDate = _portfolio.InitialDate,
                    CurrentValue = _portfolio.CurrentValue,
                    GrowthUpdates = _portfolio.GrowthUpdates.ToList(),
                    Withdrawals = _portfolio.Withdrawals.ToList()
                };
            }
        }
        
        public void UpdateInitialValue(decimal value, DateTime date)
        {
            lock (_lock)
            {
                _portfolio.InitialValue = value;
                _portfolio.InitialDate = date;
                Save();
                Log($"[PORTFOLIO] Updated initial value: {value} USDT (date: {date:yyyy-MM-dd})");
            }
        }
        
        public void UpdateCurrentValue(decimal value)
        {
            lock (_lock)
            {
                _portfolio.CurrentValue = value;
                Save();
                Log($"[PORTFOLIO] Updated current value: {value} USDT");
            }
        }
        
        public void AddGrowthUpdate(decimal value, string notes = "", DateTime? date = null)
        {
            lock (_lock)
            {
                var update = new GrowthUpdate
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = date ?? DateTime.Now,
                    Value = value,
                    Notes = notes
                };
                _portfolio.GrowthUpdates.Insert(0, update); // Most recent first
                _portfolio.CurrentValue = value; // Update current value when adding growth update
                Save();
                Log($"[PORTFOLIO] Added growth update: {value} USDT (date: {update.Date:yyyy-MM-dd HH:mm})");
            }
        }
        
        public void AddWithdrawal(decimal amount, string category, string description, string currency = "USDT")
        {
            lock (_lock)
            {
                var withdrawal = new Withdrawal
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = DateTime.Now,
                    Amount = amount,
                    Category = category,
                    Description = description,
                    Currency = currency
                };
                _portfolio.Withdrawals.Insert(0, withdrawal); // Most recent first
                Save();
                Log($"[PORTFOLIO] Added withdrawal: {amount} {currency} ({category})");
            }
        }
        
        public bool UpdateWithdrawal(string id, decimal? amount = null, string? category = null, string? description = null, string? currency = null)
        {
            lock (_lock)
            {
                var withdrawal = _portfolio.Withdrawals.FirstOrDefault(w => w.Id == id);
                if (withdrawal == null)
                {
                    Log($"[PORTFOLIO] Withdrawal not found: {id}");
                    return false;
                }
                
                if (amount.HasValue) withdrawal.Amount = amount.Value;
                if (!string.IsNullOrEmpty(category)) withdrawal.Category = category;
                if (!string.IsNullOrEmpty(description)) withdrawal.Description = description;
                if (!string.IsNullOrEmpty(currency)) withdrawal.Currency = currency;
                
                Save();
                Log($"[PORTFOLIO] Updated withdrawal: {id}");
                return true;
            }
        }
        
        public bool DeleteWithdrawal(string id)
        {
            lock (_lock)
            {
                var removed = _portfolio.Withdrawals.RemoveAll(w => w.Id == id);
                if (removed > 0)
                {
                    Save();
                    Log($"[PORTFOLIO] Deleted withdrawal: {id}");
                    return true;
                }
                return false;
            }
        }
        
        public decimal GetTotalWithdrawals()
        {
            lock (_lock)
            {
                return _portfolio.Withdrawals.Sum(w => w.Amount);
            }
        }
        
        public decimal GetTotalGrowth()
        {
            lock (_lock)
            {
                if (_portfolio.InitialValue == 0) return 0;
                return _portfolio.CurrentValue - _portfolio.InitialValue;
            }
        }
        
        public decimal GetTotalGrowthPercent()
        {
            lock (_lock)
            {
                if (_portfolio.InitialValue == 0) return 0;
                return ((_portfolio.CurrentValue - _portfolio.InitialValue) / _portfolio.InitialValue) * 100;
            }
        }
        
        private void Log(string message) => OnLog?.Invoke(message);
    }
}
