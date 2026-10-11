using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SharedData.Data;
using WinRT.Interop;

namespace DashboardUI;

public sealed partial class MainWindow : Window
{
    private readonly SessionRepository _repository;
    private readonly DispatcherTimer _autoRefreshTimer;

    public MainWindow()
    {
        this.InitializeComponent();

        // ── Mica backdrop for native Windows 11 look ──
        this.SystemBackdrop = new MicaBackdrop();

        // ── Extend content into title bar ──
        this.ExtendsContentIntoTitleBar = true;

        // ── Window sizing ──
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(520, 780));

        // ── Init database & load today's data ──
        DatabaseInitializer.EnsureCreated();
        _repository = new SessionRepository();

        DatePicker.Date = DateTimeOffset.Now;
        LoadData(DateTime.Today);

        // ── Auto-refresh every 15 seconds if viewing today ──
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoRefreshTimer.Tick += (_, _) =>
        {
            if (DatePicker.Date?.DateTime.Date == DateTime.Today)
            {
                LoadData(DateTime.Today);
            }
        };
        _autoRefreshTimer.Start();
    }

    private void DatePicker_DateChanged(CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
            LoadData(args.NewDate.Value.DateTime.Date);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var date = DatePicker.Date?.DateTime.Date ?? DateTime.Today;
        LoadData(date);
    }

    private void LoadData(DateTime date)
    {
        var summaries = _repository.GetDailySummary(date);
        var totalSeconds = _repository.GetTotalScreenTime(date);

        // ── Total time banner ──
        var totalTs = TimeSpan.FromSeconds(totalSeconds);
        TotalTimeText.Text = totalTs.TotalHours >= 1
            ? $"{(int)totalTs.TotalHours}h {totalTs.Minutes}m"
            : totalTs.TotalMinutes >= 1
                ? $"{totalTs.Minutes}m {totalTs.Seconds}s"
                : totalSeconds > 0
                    ? $"{totalTs.Seconds}s"
                    : "—";

        // ── Build app list ──
        AppListPanel.Children.Clear();

        if (summaries.Count == 0)
        {
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;
        double maxSeconds = summaries[0].TotalSeconds; // already sorted desc

        foreach (var app in summaries)
        {
            var card = CreateAppCard(app.ProcessName, app.FormattedDuration,
                app.TotalSeconds, maxSeconds);
            AppListPanel.Children.Add(card);
        }
    }

    /// <summary>
    /// Creates a single app usage card with name, duration, and proportional progress bar.
    /// </summary>
    private static Border CreateAppCard(string processName, string duration,
        double seconds, double maxSeconds)
    {
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = maxSeconds,
            Value = seconds,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = new SolidColorBrush(Colors.DodgerBlue)
        };

        var nameBlock = new TextBlock
        {
            Text = processName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        };

        var durationBlock = new TextBlock
        {
            Text = duration,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(nameBlock, 0);
        Grid.SetColumn(durationBlock, 1);
        headerRow.Children.Add(nameBlock);
        headerRow.Children.Add(durationBlock);

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(headerRow);
        stack.Children.Add(progressBar);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12),
            Child = stack
        };
    }
}

