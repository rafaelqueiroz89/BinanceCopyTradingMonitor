using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
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
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portfolio.xlsx");
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
                        LoadFromExcel();
                        Log($"[PORTFOLIO] Loaded portfolio data from Excel (Initial: {_portfolio.InitialValue}, Current: {_portfolio.CurrentValue})");
                    }
                    else
                    {
                        InitializeWithSampleData();
                        Log($"[PORTFOLIO] Initialized with sample data");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PORTFOLIO] Error loading: {ex.Message}");
                    _portfolio = new PortfolioData();
                }
            }
        }

        private void InitializeWithSampleData()
        {
            _portfolio = new PortfolioData
            {
                InitialValue = 31044m,
                InitialDate = new DateTime(2025, 12, 6),
                CurrentValue = 36750m
            };

            // Add growth updates
            var growthUpdates = new[]
            {
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2025, 12, 6), Value = 31044m, Notes = "Initial value" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2025, 12, 10), Value = 32711m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2025, 12, 15), Value = 33244m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2025, 12, 20), Value = 34098m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2025, 12, 25), Value = 34333m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2025, 12, 30), Value = 33071m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2025, 12, 31), Value = 34209m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2026, 1, 1), Value = 34303m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2026, 1, 2), Value = 34759m, Notes = "Growth update" },
                new GrowthUpdate { Id = Guid.NewGuid().ToString(), Date = new DateTime(2026, 1, 3), Value = 36750m, Notes = "Growth update" }
            };

            foreach (var update in growthUpdates)
            {
                _portfolio.GrowthUpdates.Add(update);
            }

            // Add withdrawal
            var withdrawal = new Withdrawal
            {
                Id = Guid.NewGuid().ToString(),
                Date = new DateTime(2025, 12, 1), // December 1st, 2025
                Amount = 496m,
                Category = "voucher_uber",
                Description = "Uber Eats voucher",
                Currency = "USDT"
            };
            _portfolio.Withdrawals.Add(withdrawal);

            Save(); // Save the initial data
        }

        private void LoadFromExcel()
        {
            using var workbook = new XLWorkbook(_filePath);

            // Load portfolio summary
            var summarySheet = workbook.Worksheet("Summary");
            if (summarySheet != null)
            {
                _portfolio.InitialValue = summarySheet.Cell("B1").GetValue<decimal>();
                _portfolio.InitialDate = summarySheet.Cell("B2").GetValue<DateTime>();
                _portfolio.CurrentValue = summarySheet.Cell("B3").GetValue<decimal>();
            }

            // Load growth updates
            var growthSheet = workbook.Worksheet("Growth Updates");
            if (growthSheet != null)
            {
                _portfolio.GrowthUpdates.Clear();
                for (int row = 2; row <= growthSheet.LastRowUsed().RowNumber(); row++)
                {
                    var update = new GrowthUpdate
                    {
                        Id = growthSheet.Cell(row, 1).GetValue<string>(),
                        Date = growthSheet.Cell(row, 2).GetValue<DateTime>(),
                        Value = growthSheet.Cell(row, 3).GetValue<decimal>(),
                        Notes = growthSheet.Cell(row, 4).GetValue<string>()
                    };
                    _portfolio.GrowthUpdates.Add(update);
                }
            }

            // Load withdrawals
            var withdrawalSheet = workbook.Worksheet("Withdrawals");
            if (withdrawalSheet != null)
            {
                _portfolio.Withdrawals.Clear();
                for (int row = 2; row <= withdrawalSheet.LastRowUsed().RowNumber(); row++)
                {
                    var withdrawal = new Withdrawal
                    {
                        Id = withdrawalSheet.Cell(row, 1).GetValue<string>(),
                        Date = withdrawalSheet.Cell(row, 2).GetValue<DateTime>(),
                        Amount = withdrawalSheet.Cell(row, 3).GetValue<decimal>(),
                        Category = withdrawalSheet.Cell(row, 4).GetValue<string>(),
                        Description = withdrawalSheet.Cell(row, 5).GetValue<string>(),
                        Currency = withdrawalSheet.Cell(row, 6).GetValue<string>()
                    };
                    _portfolio.Withdrawals.Add(withdrawal);
                }
            }
        }



        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    using var workbook = new XLWorkbook();

                    // Summary sheet
                    var summarySheet = workbook.Worksheets.Add("Summary");
                    summarySheet.Cell("A1").Value = "Initial Value (USDT)";
                    summarySheet.Cell("B1").Value = _portfolio.InitialValue;
                    summarySheet.Cell("A2").Value = "Initial Date";
                    summarySheet.Cell("B2").Value = _portfolio.InitialDate;
                    summarySheet.Cell("A3").Value = "Current Value (USDT)";
                    summarySheet.Cell("B3").Value = _portfolio.CurrentValue;

                    // Growth Updates sheet
                    var growthSheet = workbook.Worksheets.Add("Growth Updates");
                    growthSheet.Cell("A1").Value = "ID";
                    growthSheet.Cell("B1").Value = "Date";
                    growthSheet.Cell("C1").Value = "Value (USDT)";
                    growthSheet.Cell("D1").Value = "Notes";

                    for (int i = 0; i < _portfolio.GrowthUpdates.Count; i++)
                    {
                        var update = _portfolio.GrowthUpdates[i];
                        growthSheet.Cell(i + 2, 1).Value = update.Id;
                        growthSheet.Cell(i + 2, 2).Value = update.Date;
                        growthSheet.Cell(i + 2, 3).Value = update.Value;
                        growthSheet.Cell(i + 2, 4).Value = update.Notes;
                    }

                    // Withdrawals sheet
                    var withdrawalSheet = workbook.Worksheets.Add("Withdrawals");
                    withdrawalSheet.Cell("A1").Value = "ID";
                    withdrawalSheet.Cell("B1").Value = "Date";
                    withdrawalSheet.Cell("C1").Value = "Amount";
                    withdrawalSheet.Cell("D1").Value = "Category";
                    withdrawalSheet.Cell("E1").Value = "Description";
                    withdrawalSheet.Cell("F1").Value = "Currency";

                    for (int i = 0; i < _portfolio.Withdrawals.Count; i++)
                    {
                        var withdrawal = _portfolio.Withdrawals[i];
                        withdrawalSheet.Cell(i + 2, 1).Value = withdrawal.Id;
                        withdrawalSheet.Cell(i + 2, 2).Value = withdrawal.Date;
                        withdrawalSheet.Cell(i + 2, 3).Value = withdrawal.Amount;
                        withdrawalSheet.Cell(i + 2, 4).Value = withdrawal.Category;
                        withdrawalSheet.Cell(i + 2, 5).Value = withdrawal.Description;
                        withdrawalSheet.Cell(i + 2, 6).Value = withdrawal.Currency;
                    }

                    // Auto-fit columns
                    summarySheet.Columns().AdjustToContents();
                    growthSheet.Columns().AdjustToContents();
                    withdrawalSheet.Columns().AdjustToContents();

                    workbook.SaveAs(_filePath);
                }
                catch (Exception ex)
                {
                    Log($"[PORTFOLIO] Error saving to Excel: {ex.Message}");
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
