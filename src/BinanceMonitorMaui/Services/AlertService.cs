using BinanceMonitorMaui.Models;
using Plugin.LocalNotification;
using System.Text.Json;

namespace BinanceMonitorMaui.Services
{
    public class AlertService
    {
        private const string AlertsKey = "position_alerts_v2";
        private const string QuietStartKey = "quiet_hours_start";
        private const string QuietEndKey = "quiet_hours_end";
        private const string QuietEnabledKey = "quiet_hours_enabled";
        
        private Dictionary<string, List<PositionAlert>> _alerts = new();
        private HashSet<string> _knownPositions = new();
        private int _notificationId = 1000;
        
        public event Action<string, string>? OnAlertTriggered;
        
        // Quiet hours settings (default: 00:00 to 09:00)
        public int QuietStartHour 
        { 
            get => Preferences.Get(QuietStartKey, 0);
            set => Preferences.Set(QuietStartKey, value);
        }
        
        public int QuietEndHour 
        { 
            get => Preferences.Get(QuietEndKey, 9);
            set => Preferences.Set(QuietEndKey, value);
        }
        
        public bool QuietHoursEnabled
        {
            get => Preferences.Get(QuietEnabledKey, true);
            set => Preferences.Set(QuietEnabledKey, value);
        }
        
        public AlertService()
        {
            LoadAlerts();
        }
        
        public bool IsInQuietHours()
        {
            if (!QuietHoursEnabled) return false;
            
            var now = DateTime.Now.Hour;
            
            if (QuietStartHour <= QuietEndHour)
            {
                // Simple case: e.g., 0-9 (midnight to 9am)
                return now >= QuietStartHour && now < QuietEndHour;
            }
            else
            {
                // Wrap around case: e.g., 22-6 (10pm to 6am)
                return now >= QuietStartHour || now < QuietEndHour;
            }
        }
        
        public void SendNotification(string title, string message)
        {
            if (IsInQuietHours()) return;
            
            try
            {
                var notification = new NotificationRequest
                {
                    NotificationId = _notificationId++,
                    Title = title,
                    Description = message,
                    CategoryType = NotificationCategoryType.Status,
                    Android = new Plugin.LocalNotification.AndroidOption.AndroidOptions
                    {
                        Priority = Plugin.LocalNotification.AndroidOption.AndroidPriority.High,
                        ChannelId = "binance_alerts"
                    }
                };
                
                LocalNotificationCenter.Current.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Alert] Notification error: {ex.Message}");
            }
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
                        message = $"PnL reached {position.PnLPercentage:+0.00}% (target: >{alert.Threshold}%)";
                    }
                    else if (!alert.IsAbove && position.PnLPercentage <= alert.Threshold)
                    {
                        shouldTrigger = true;
                        message = $"PnL dropped to {position.PnLPercentage:+0.00}% (target: <{alert.Threshold}%)";
                    }
                }
                
                if (shouldTrigger)
                {
                    alert.Triggered = true;
                    SaveAlerts();
                    
                    // Send notification to tray instead of popup
                    SendNotification($"🔔 {position.Symbol}", message);
                    
                    // Also fire event for any listeners
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
