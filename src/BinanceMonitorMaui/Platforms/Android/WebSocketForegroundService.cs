using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace BinanceMonitorMaui;

[Service(Name = "com.binance.monitor.WebSocketForegroundService", ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public class WebSocketForegroundService : Service
{
    private const int NotificationId = 1000;
    private const string ChannelId = "websocket_service";
    private const string ChannelName = "WebSocket Connection";
    private PowerManager.WakeLock? _wakeLock;
    
    public static WebSocketForegroundService? Instance { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Instance = this;
        
        var status = intent?.GetStringExtra("status") ?? "Connecting...";
        
        CreateNotificationChannel();
        StartForeground(NotificationId, CreateNotification(status), Android.Content.PM.ForegroundService.TypeDataSync);
        AcquireWakeLock();
        
        return StartCommandResult.Sticky;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
            {
                Description = "Keeps WebSocket connection alive"
            };
            
            var notificationManager = GetSystemService(NotificationService) as NotificationManager;
            notificationManager?.CreateNotificationChannel(channel);
        }
    }

    private Notification CreateNotification(string status)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop);
        
        var pendingIntent = PendingIntent.GetActivity(
            this, 0, intent, 
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Binance Monitor")
            .SetContentText(status)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(true)
            .SetContentIntent(pendingIntent)
            .SetPriority(NotificationCompat.PriorityLow);

        return builder.Build();
    }

    private void AcquireWakeLock()
    {
        var powerManager = GetSystemService(PowerService) as PowerManager;
        _wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, "BinanceMonitor::WebSocketLock");
        _wakeLock?.Acquire();
    }

    public override void OnDestroy()
    {
        Instance = null;
        _wakeLock?.Release();
        base.OnDestroy();
    }

    public void UpdateStatus(string status)
    {
        var notification = CreateNotification(status);
        var notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.Notify(NotificationId, notification);
    }
    
    public static void UpdateNotificationStatus(string status)
    {
        Instance?.UpdateStatus(status);
    }
}
