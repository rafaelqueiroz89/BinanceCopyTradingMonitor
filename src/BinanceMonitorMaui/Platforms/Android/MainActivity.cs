using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace BinanceMonitorMaui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, 
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public static MainActivity? Instance { get; private set; }
    private const int PermissionRequestCode = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Instance = this;
        
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        
        RequestPermissions();
        RequestBatteryOptimizationExemption();
    }

    private void RequestPermissions()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.PostNotifications }, PermissionRequestCode);
            }
        }
    }

    private void RequestBatteryOptimizationExemption()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var powerManager = GetSystemService(PowerService) as PowerManager;
            var packageName = PackageName;
            
            if (powerManager != null && packageName != null && !powerManager.IsIgnoringBatteryOptimizations(packageName))
            {
                var intent = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(Android.Net.Uri.Parse($"package:{packageName}"));
                StartActivity(intent);
            }
        }
    }

    public void StartForegroundService()
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

    public void StopForegroundService()
    {
        var intent = new Intent(this, typeof(WebSocketForegroundService));
        StopService(intent);
    }
}
