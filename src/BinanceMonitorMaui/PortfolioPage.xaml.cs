using BinanceMonitorMaui.Models;
using BinanceMonitorMaui.Services;
using System.Collections.ObjectModel;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;

namespace BinanceMonitorMaui;

public partial class PortfolioPage : ContentPage
{
    private readonly WebSocketService _webSocket;
    private PortfolioData _portfolio = new();
    private readonly ObservableCollection<GrowthUpdate> _growthUpdates = new();
    private readonly ObservableCollection<Withdrawal> _withdrawals = new();
    
    public ObservableCollection<GrowthUpdate> GrowthUpdates => _growthUpdates;
    public ObservableCollection<Withdrawal> Withdrawals => _withdrawals;
    
    public PortfolioPage()
    {
        InitializeComponent();
        
        // Use the shared WebSocketService instance
        _webSocket = AppShell.Instance?.GetWebSocketService() ?? WebSocketService.Instance;
        
        GrowthUpdatesCollection.ItemsSource = _growthUpdates;
        WithdrawalsCollection.ItemsSource = _withdrawals;
        
        // Subscribe to WebSocket events
        _webSocket.OnPortfolioDataReceived += OnPortfolioDataReceived;
        _webSocket.OnPortfolioUpdateResult += OnPortfolioUpdateResult;
        
        // Load portfolio on appear
        this.Appearing += async (s, e) => 
        {
            await ConnectAndLoadPortfolioAsync();
        };
    }
    
    private async Task ConnectAndLoadPortfolioAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[PORTFOLIO] WebSocket connected: {_webSocket.IsConnected}");
            
            // If already connected (from MainPage), just load the portfolio
            // Otherwise, the connection will be established by MainPage
            if (_webSocket.IsConnected)
            {
                await LoadPortfolioAsync();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[PORTFOLIO] WebSocket not connected yet, waiting...");
                // Wait a bit and try again - MainPage should connect
                await Task.Delay(1000);
                if (_webSocket.IsConnected)
                {
                    await LoadPortfolioAsync();
                }
                else
                {
                    await DisplayAlert("Not Connected", "Please connect from the main page first", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PORTFOLIO] Connection error: {ex.Message}");
            await DisplayAlert("Error", $"Failed to load portfolio: {ex.Message}", "OK");
        }
    }
    
    private async Task LoadPortfolioAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[PORTFOLIO] Requesting portfolio data...");
            await _webSocket.SendGetPortfolioAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PORTFOLIO] Load error: {ex.Message}");
            await DisplayAlert("Error", $"Failed to load portfolio: {ex.Message}", "OK");
        }
    }
    
    private void OnPortfolioDataReceived(PortfolioData portfolio)
    {
        System.Diagnostics.Debug.WriteLine($"[PORTFOLIO] Data received - Initial: {portfolio.InitialValue}, Current: {portfolio.CurrentValue}, Updates: {portfolio.GrowthUpdates.Count}, Withdrawals: {portfolio.Withdrawals.Count}");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _portfolio = portfolio;
            UpdateUI();
        });
    }
    
    private void OnPortfolioUpdateResult(bool success, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (success)
            {
                // Reload portfolio after update
                await LoadPortfolioAsync();
            }
            else
            {
                await DisplayAlert("Error", message, "OK");
            }
        });
    }
    
    private void UpdateUI()
    {
        InitialValueLabel.Text = $"{_portfolio.InitialValue:0.00} USDT";
        CurrentValueLabel.Text = $"{_portfolio.CurrentValue:0.00} USDT";
        InitialDateLabel.Text = $"Initial Date: {_portfolio.InitialDate:yyyy-MM-dd}";
        
        var growth = _portfolio.TotalGrowth;
        var growthPercent = _portfolio.TotalGrowthPercent;
        TotalGrowthLabel.Text = $"{growth:+0.00;-0.00} USDT ({growthPercent:+0.00;-0.00}%)";
        TotalGrowthLabel.TextColor = growth >= 0 ? Color.FromArgb("#4ade80") : Color.FromArgb("#e94560");
        
        TotalWithdrawalsLabel.Text = $"{_portfolio.TotalWithdrawals:0.00} USDT";
        
        // Update collections
        _growthUpdates.Clear();
        foreach (var update in _portfolio.GrowthUpdates)
        {
            _growthUpdates.Add(update);
        }
        
        _withdrawals.Clear();
        foreach (var withdrawal in _portfolio.Withdrawals)
        {
            _withdrawals.Add(withdrawal);
        }
        
        NoGrowthUpdatesLabel.IsVisible = _growthUpdates.Count == 0;
        NoWithdrawalsLabel.IsVisible = _withdrawals.Count == 0;
    }
    
    private async void OnEditInitialValueClicked(object? sender, EventArgs e)
    {
        var valueStr = await DisplayPromptAsync(
            "Edit Initial Value",
            "Enter initial portfolio value (USDT):",
            initialValue: _portfolio.InitialValue.ToString("0.00"),
            keyboard: Keyboard.Numeric);
        
        if (string.IsNullOrEmpty(valueStr) || !decimal.TryParse(valueStr, out var value))
            return;
        
        // Use DatePicker dialog
        var date = await ShowDatePickerDialog(_portfolio.InitialDate);
        
        await _webSocket.SendUpdateInitialValueAsync(value, date);
    }
    
    private async Task<DateTime> ShowDatePickerDialog(DateTime initialDate)
    {
        var tcs = new TaskCompletionSource<DateTime>();
        var selectedDate = initialDate;
        
        var datePicker = new DatePicker
        {
            Date = initialDate,
            MinimumDate = new DateTime(2020, 1, 1),
            MaximumDate = DateTime.Now
        };
        
        datePicker.DateSelected += (s, e) =>
        {
            selectedDate = e.NewDate;
        };
        
        var okButton = new Button { Text = "OK", BackgroundColor = Color.FromArgb("#1e3a5f"), TextColor = Colors.White };
        okButton.Clicked += (s, e) =>
        {
            tcs.SetResult(selectedDate);
            Shell.Current.Navigation.PopModalAsync();
        };
        
        var cancelButton = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#5f1e3a"), TextColor = Colors.White };
        cancelButton.Clicked += (s, e) =>
        {
            tcs.SetResult(initialDate);
            Shell.Current.Navigation.PopModalAsync();
        };
        
        var dialog = new ContentPage
        {
            Title = "Select Date",
            BackgroundColor = Color.FromArgb("#0f0f1a"),
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 20,
                Children =
                {
                    new Label { Text = "Select Initial Date", FontSize = 18, TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
                    datePicker,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };
        
        await Shell.Current.Navigation.PushModalAsync(dialog);
        return await tcs.Task;
    }
    
    private async void OnUpdateCurrentValueClicked(object? sender, EventArgs e)
    {
        var valueStr = await DisplayPromptAsync(
            "Update Current Value",
            "Enter current portfolio value (USDT):",
            initialValue: _portfolio.CurrentValue.ToString("0.00"),
            keyboard: Keyboard.Numeric);
        
        if (string.IsNullOrEmpty(valueStr) || !decimal.TryParse(valueStr, out var value))
            return;
        
        await _webSocket.SendUpdateCurrentValueAsync(value);
    }
    
    
    private async void OnAddWithdrawalClicked(object? sender, EventArgs e)
    {
        var amountStr = await DisplayPromptAsync(
            "Add Withdrawal",
            "Enter withdrawal amount (USDT):",
            keyboard: Keyboard.Numeric);
        
        if (string.IsNullOrEmpty(amountStr) || !decimal.TryParse(amountStr, out var amount))
            return;
        
        var category = await DisplayActionSheet(
            "Select Category",
            "Cancel",
            null,
            "💳 Credit Card",
            "💶 EUR",
            "💵 BRL",
            "💱 Other Fiat",
            "🍔 Uber Eats",
            "🎫 Other Voucher");
        
        if (category == "Cancel" || string.IsNullOrEmpty(category))
            return;
        
        var categoryCode = category switch
        {
            "💳 Credit Card" => Withdrawal.Categories.CreditCard,
            "💶 EUR" => Withdrawal.Categories.FiatEur,
            "💵 BRL" => Withdrawal.Categories.FiatBrl,
            "💱 Other Fiat" => Withdrawal.Categories.FiatOther,
            "🍔 Uber Eats" => Withdrawal.Categories.VoucherUber,
            "🎫 Other Voucher" => Withdrawal.Categories.VoucherOther,
            _ => ""
        };
        
        var currency = categoryCode.StartsWith("fiat_") 
            ? (categoryCode == Withdrawal.Categories.FiatEur ? "EUR" : categoryCode == Withdrawal.Categories.FiatBrl ? "BRL" : "USD")
            : "USDT";
        
        // Add date picker for withdrawal
        var date = await ShowDatePickerDialog(DateTime.Now);
        
        var description = await DisplayPromptAsync(
            "Description",
            "Enter description:",
            initialValue: "",
            keyboard: Keyboard.Default);
        
        await _webSocket.SendAddWithdrawalAsync(amount, categoryCode, description ?? "", currency, date);
    }
    
    private async void OnEditWithdrawalClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is Withdrawal withdrawal)
        {
            var amountStr = await DisplayPromptAsync(
                "Edit Withdrawal",
                "Enter withdrawal amount:",
                initialValue: withdrawal.Amount.ToString("0.00"),
                keyboard: Keyboard.Numeric);
            
            if (string.IsNullOrEmpty(amountStr) || !decimal.TryParse(amountStr, out var amount))
                return;
            
            var category = await DisplayActionSheet(
                "Select Category",
                "Cancel",
                null,
                "💳 Credit Card",
                "💶 EUR",
                "💵 BRL",
                "💱 Other Fiat",
                "🍔 Uber Eats",
                "🎫 Other Voucher");
            
            if (category == "Cancel" || string.IsNullOrEmpty(category))
                return;
            
            var categoryCode = category switch
            {
                "💳 Credit Card" => Withdrawal.Categories.CreditCard,
                "💶 EUR" => Withdrawal.Categories.FiatEur,
                "💵 BRL" => Withdrawal.Categories.FiatBrl,
                "💱 Other Fiat" => Withdrawal.Categories.FiatOther,
                "🍔 Uber Eats" => Withdrawal.Categories.VoucherUber,
                "🎫 Other Voucher" => Withdrawal.Categories.VoucherOther,
                _ => withdrawal.Category
            };
            
            var currency = categoryCode.StartsWith("fiat_") 
                ? (categoryCode == Withdrawal.Categories.FiatEur ? "EUR" : categoryCode == Withdrawal.Categories.FiatBrl ? "BRL" : "USD")
                : withdrawal.Currency;
            
            // Add date picker for withdrawal edit
            var date = await ShowDatePickerDialog(withdrawal.Date);
            
            var description = await DisplayPromptAsync(
                "Description",
                "Enter description:",
                initialValue: withdrawal.Description,
                keyboard: Keyboard.Default);
            
            await _webSocket.SendUpdateWithdrawalAsync(withdrawal.Id, amount, categoryCode, description ?? "", currency, date);
        }
    }
    
    private async void OnDeleteWithdrawalClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is Withdrawal withdrawal)
        {
            var confirm = await DisplayAlert(
                "Delete Withdrawal",
                "Are you sure you want to delete this withdrawal?",
                "Delete",
                "Cancel");
            
            if (confirm)
            {
                await _webSocket.SendDeleteWithdrawalAsync(withdrawal.Id);
            }
        }
    }
    
    private async void OnSwipeLeft(object? sender, SwipedEventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
    }
    
    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
    }
}
