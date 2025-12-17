using BinanceMonitorMaui.Models;
using System.Text.Json;

namespace BinanceMonitorMaui.Services
{
    public class AlertService
    {
        private const string AlertsKey = "position_alerts_v2";
        private Dictionary<string, List<PositionAlert>> _alerts = new();
        private HashSet<string> _knownPositions = new();
        
        public event Action<string, string>? OnAlertTriggered;
        
        public AlertService()
        {
            LoadAlerts();
        }
        
        public void EnsureDefaultAlert(string positionKey)
        {
            if (_knownPositions.Contains(positionKey)) return;
            
            _knownPositions.Add(positionKey);
            
            if (!_alerts.ContainsKey(positionKey) || _alerts[positionKey].Count == 0)
            {
                AddAlert(positionKey, "pnl_percent", 5, true);
            }
        }
        
        public void AddAlert(string positionKey, string alertType, decimal threshold, bool isAbove)
        {
            if (!_alerts.ContainsKey(positionKey))
                _alerts[positionKey] = new List<PositionAlert>();
            
            var exists = _alerts[positionKey].Any(a => 
                a.AlertType == alertType && a.Threshold == threshold && a.IsAbove == isAbove);
            
            if (!exists)
            {
                _alerts[positionKey].Add(new PositionAlert
                {
                    PositionKey = positionKey,
                    AlertType = alertType,
                    Threshold = threshold,
                    IsAbove = isAbove,
                    Triggered = false
                });
                
                SaveAlerts();
            }
        }
        
        public void RemoveAlert(string positionKey, int index)
        {
            if (_alerts.ContainsKey(positionKey) && index < _alerts[positionKey].Count)
            {
                _alerts[positionKey].RemoveAt(index);
                SaveAlerts();
            }
        }
        
        public void RemoveAllAlerts(string positionKey)
        {
            if (_alerts.ContainsKey(positionKey))
            {
                _alerts.Remove(positionKey);
                SaveAlerts();
            }
        }
        
        public List<PositionAlert> GetAlerts(string positionKey)
        {
            return _alerts.TryGetValue(positionKey, out var alerts) ? alerts : new List<PositionAlert>();
        }
        
        public bool HasAlerts(string positionKey)
        {
            return _alerts.ContainsKey(positionKey) && _alerts[positionKey].Count > 0;
        }
        
        public int GetAlertCount(string positionKey)
        {
            return _alerts.TryGetValue(positionKey, out var alerts) ? alerts.Count : 0;
        }
        
        public string GetAlertIndicator(string positionKey)
        {
            var count = GetAlertCount(positionKey);
            return count > 0 ? $"🔔{count}" : "";
        }
        
        public void CheckAlerts(Position position)
        {
            var key = position.UniqueKey;
            if (!_alerts.ContainsKey(key)) return;
            
            foreach (var alert in _alerts[key].Where(a => !a.Triggered))
            {
                bool shouldTrigger = false;
                string message = "";
                
                if (alert.AlertType == "pnl_percent")
                {
                    if (alert.IsAbove && position.PnLPercentage >= alert.Threshold)
                    {
                        shouldTrigger = true;
                        message = $"{position.Symbol}\nPnL reached {position.PnLPercentage:+0.00}%\n(target: >{alert.Threshold}%)";
                    }
                    else if (!alert.IsAbove && position.PnLPercentage <= alert.Threshold)
                    {
                        shouldTrigger = true;
                        message = $"{position.Symbol}\nPnL dropped to {position.PnLPercentage:+0.00}%\n(target: <{alert.Threshold}%)";
                    }
                }
                
                if (shouldTrigger)
                {
                    alert.Triggered = true;
                    SaveAlerts();
                    OnAlertTriggered?.Invoke(position.Symbol, message);
                }
            }
        }
        
        public void ResetTriggeredAlerts(string positionKey)
        {
            if (_alerts.ContainsKey(positionKey))
            {
                foreach (var alert in _alerts[positionKey])
                    alert.Triggered = false;
                SaveAlerts();
            }
        }
        
        private void SaveAlerts()
        {
            try
            {
                var json = JsonSerializer.Serialize(_alerts);
                Preferences.Set(AlertsKey, json);
            }
            catch { }
        }
        
        private void LoadAlerts()
        {
            try
            {
                var json = Preferences.Get(AlertsKey, "{}");
                _alerts = JsonSerializer.Deserialize<Dictionary<string, List<PositionAlert>>>(json) ?? new();
            }
            catch
            {
                _alerts = new();
            }
        }
    }
}
