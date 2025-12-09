using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace BinanceMonitorMaui.Platforms.Android;

[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public class WebSocketForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "binance_monitor_channel";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();
        
        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Binance Monitor")
            .SetContentText("Monitoring positions...")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityLow)
            .Build();

        StartForeground(NotificationId, notification);

        return StartCommandResult.Sticky;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "Binance Monitor",
                NotificationImportance.Low)
            {
                Description = "Keeps WebSocket connection alive"
            };

            var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            notificationManager?.CreateNotificationChannel(channel);
        }
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }
}

