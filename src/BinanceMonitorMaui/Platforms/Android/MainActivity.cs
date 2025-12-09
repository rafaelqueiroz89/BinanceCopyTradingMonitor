using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using BinanceMonitorMaui.Platforms.Android;

namespace BinanceMonitorMaui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        // Request notification permission for Android 13+
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 0);
        }
        
        // Start foreground service to keep WebSocket alive
        StartForegroundService();
        
        // Request to ignore battery optimizations
        RequestIgnoreBatteryOptimization();
    }

    private void StartForegroundService()
    {
        var intent = new Intent(this, typeof(WebSocketForegroundService));
        
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            StartForegroundService(intent);
        }
        else
        {
            StartService(intent);
        }
    }

    private void RequestIgnoreBatteryOptimization()
    {
        try
        {
            var pm = (PowerManager?)GetSystemService(PowerService);
            var packageName = PackageName;
            
            if (pm != null && packageName != null && !pm.IsIgnoringBatteryOptimizations(packageName))
            {
                var intent = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(Android.Net.Uri.Parse($"package:{packageName}"));
                StartActivity(intent);
            }
        }
        catch { }
    }

    protected override void OnDestroy()
    {
        // Keep the service running even when app is closed
        base.OnDestroy();
    }
}
