using Plugin.LocalNotification;

namespace BinanceMonitorMaui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		ScheduleDailyReminder();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
	
	private void ScheduleDailyReminder()
	{
		try
		{
			// Cancel any existing reminder
			LocalNotificationCenter.Current.Cancel(9999);
			
			// Schedule daily reminder at 23:00
			var notification = new NotificationRequest
			{
				NotificationId = 9999,
				Title = "💰 Portfolio Tracker Reminder",
				Description = "Don't forget to update your portfolio value for today!",
				Schedule = new NotificationRequestSchedule
				{
					NotifyTime = DateTime.Today.AddHours(23),
					RepeatType = NotificationRepeat.Daily
				},
				CategoryType = NotificationCategoryType.Reminder,
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
			System.Diagnostics.Debug.WriteLine($"[REMINDER] Error scheduling: {ex.Message}");
		}
	}
}