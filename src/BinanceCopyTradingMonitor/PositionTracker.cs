using System;
using System.Collections.Generic;
using System.Linq;

namespace BinanceCopyTradingMonitor
{
    public class TrackedPosition
    {
        public string Key => $"{Trader}:{Symbol}";
        public string Trader { get; set; } = "";
        public string Symbol { get; set; } = "";
        public DateTime FirstSeen { get; set; }
        public decimal InitialPnLPercentage { get; set; }
        public decimal CurrentPnLPercentage { get; set; }
        public decimal CurrentPnL { get; set; }
        public decimal PeakPnLPercentage { get; set; }
        public bool QuickGainerAlertSent { get; set; }
        public bool ExplosionAlertSent { get; set; }
        public decimal Growth => CurrentPnLPercentage - InitialPnLPercentage;
    }

    public class QuickGainerAlert
    {
        public string Trader { get; set; } = "";
        public string Symbol { get; set; } = "";
        public decimal CurrentPnLPercentage { get; set; }
        public decimal Growth { get; set; }
        public decimal PnL { get; set; }
        public string AlertType { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class PositionTracker
    {
        private readonly Dictionary<string, TrackedPosition> _positions = new();
        private readonly object _lock = new();

        public decimal QuickGainerThreshold { get; set; } = 10m;
        public decimal ExplosionThreshold { get; set; } = 20m;

        public event Action<QuickGainerAlert>? OnQuickGainer;
        public event Action<string>? OnLog;

        public void UpdatePositions(List<ScrapedPosition> positions)
        {
            lock (_lock)
            {
                var currentKeys = positions.Select(p => $"{p.Trader}:{p.Symbol}").ToHashSet();

                var closedKeys = _positions.Keys.Where(k => !currentKeys.Contains(k)).ToList();
                foreach (var key in closedKeys)
                {
                    _positions.Remove(key);
                    Log($"Position closed: {key}");
                }

                foreach (var pos in positions)
                {
                    var key = $"{pos.Trader}:{pos.Symbol}";

                    if (_positions.TryGetValue(key, out var tracked))
                    {
                        tracked.CurrentPnL = pos.PnL;
                        tracked.CurrentPnLPercentage = pos.PnLPercentage;

                        if (pos.PnLPercentage > tracked.PeakPnLPercentage)
                            tracked.PeakPnLPercentage = pos.PnLPercentage;

                        CheckAlerts(tracked);
                    }
                    else
                    {
                        var newTracked = new TrackedPosition
                        {
                            Trader = pos.Trader,
                            Symbol = pos.Symbol,
                            FirstSeen = DateTime.Now,
                            InitialPnLPercentage = pos.PnLPercentage,
                            CurrentPnLPercentage = pos.PnLPercentage,
                            CurrentPnL = pos.PnL,
                            PeakPnLPercentage = pos.PnLPercentage
                        };
                        _positions[key] = newTracked;
                        Log($"Tracking: {pos.Trader} | {pos.Symbol} @ {pos.PnLPercentage:+0.00;-0.00}%");
                        CheckInitialValue(newTracked);
                    }
                }
            }
        }

        private void CheckInitialValue(TrackedPosition pos)
        {
            if (pos.InitialPnLPercentage <= 0) return;

            if (pos.InitialPnLPercentage >= ExplosionThreshold)
            {
                pos.ExplosionAlertSent = true;
                pos.QuickGainerAlertSent = true;
                
                var alert = new QuickGainerAlert
                {
                    Trader = pos.Trader,
                    Symbol = pos.Symbol,
                    CurrentPnLPercentage = pos.CurrentPnLPercentage,
                    Growth = pos.InitialPnLPercentage,
                    PnL = pos.CurrentPnL,
                    AlertType = "explosion",
                    Message = $"🚀 EXPLOSION! {pos.Symbol} already at {pos.CurrentPnLPercentage:+0.00}%!"
                };
                Log(alert.Message);
                OnQuickGainer?.Invoke(alert);
            }
            else if (pos.InitialPnLPercentage >= QuickGainerThreshold)
            {
                pos.QuickGainerAlertSent = true;
                
                var alert = new QuickGainerAlert
                {
                    Trader = pos.Trader,
                    Symbol = pos.Symbol,
                    CurrentPnLPercentage = pos.CurrentPnLPercentage,
                    Growth = pos.InitialPnLPercentage,
                    PnL = pos.CurrentPnL,
                    AlertType = "quick_gainer",
                    Message = $"🔥 Hot entry! {pos.Symbol} already at {pos.CurrentPnLPercentage:+0.00}%!"
                };
                Log(alert.Message);
                OnQuickGainer?.Invoke(alert);
            }
        }

        private void CheckAlerts(TrackedPosition pos)
        {
            if (pos.Growth <= 0) return;

            if (!pos.ExplosionAlertSent && pos.Growth >= ExplosionThreshold)
            {
                pos.ExplosionAlertSent = true;
                pos.QuickGainerAlertSent = true;
                
                var alert = new QuickGainerAlert
                {
                    Trader = pos.Trader,
                    Symbol = pos.Symbol,
                    CurrentPnLPercentage = pos.CurrentPnLPercentage,
                    Growth = pos.Growth,
                    PnL = pos.CurrentPnL,
                    AlertType = "explosion",
                    Message = $"🚀 EXPLOSION! {pos.Symbol} grew {pos.Growth:+0.00}% (now at {pos.CurrentPnLPercentage:+0.00}%)"
                };
                Log(alert.Message);
                OnQuickGainer?.Invoke(alert);
            }
            else if (!pos.QuickGainerAlertSent && pos.Growth >= QuickGainerThreshold)
            {
                pos.QuickGainerAlertSent = true;
                
                var alert = new QuickGainerAlert
                {
                    Trader = pos.Trader,
                    Symbol = pos.Symbol,
                    CurrentPnLPercentage = pos.CurrentPnLPercentage,
                    Growth = pos.Growth,
                    PnL = pos.CurrentPnL,
                    AlertType = "quick_gainer",
                    Message = $"🔥 Growing! {pos.Symbol} grew {pos.Growth:+0.00}% (now at {pos.CurrentPnLPercentage:+0.00}%)"
                };
                Log(alert.Message);
                OnQuickGainer?.Invoke(alert);
            }
        }

        public List<TrackedPosition> GetAllPositions()
        {
            lock (_lock)
            {
                return _positions.Values.ToList();
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[TRACKER] {message}");
        }
    }
}
