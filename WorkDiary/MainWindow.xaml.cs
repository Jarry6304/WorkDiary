using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WorkDiary.Data;
using WorkDiary.Services;

namespace WorkDiary;

public partial class MainWindow : Window
{
    // ── 服務層 ──
    private readonly AppDbContext  _db;
    private readonly DiaryService  _diaryService;
    private readonly FileService   _fileService;

    // ── 目前日期 ──
    private DateTime _currentDate = DateTime.Today;

    // ── 自動儲存防抖（2 秒無輸入後存入 SQLite）──
    private readonly DispatcherTimer _autoSaveTimer;

    // ── 浮動月曆 ──
    private readonly FloatingCalendarWindow _calendarWindow;

    // ── 每日折疊狀態（key = date.Date）──
    private readonly Dictionary<DateTime, bool> _collapsedDates = new();

    // ── 面板模式 ──
    private bool _isBrowseMode = false;

    // ── 模式底色 ──
    internal static readonly SolidColorBrush EditModeBg =
        new(Color.FromRgb(0xEB, 0xF9, 0xEF));   // 淡綠 #EBF9EF
    internal static readonly SolidColorBrush BrowseModeBg =
        new(Color.FromRgb(0xEB, 0xF0, 0xFB));   // 淡藍 #EBF0FB

    // ── 標籤管理 ──
    private List<string> _currentTags = new();
    private readonly TextBox    _tagInputBox;
    private readonly TextBlock  _tagInputPlaceholder;
    private readonly Grid       _tagInputContainer;

    public MainWindow()
    {
        InitializeComponent();

        _db = new AppDbContext();
        _db.Database.EnsureCreated();

        // 相容舊 DB：新增欄位（若已存在則略過）
        try { _db.Database.ExecuteSqlRaw(
            "ALTER TABLE DiaryEntries ADD COLUMN IsPinned INTEGER NOT NULL DEFAULT 0"); }
        catch { /* 欄位已存在，忽略 */ }

        try { _db.Database.ExecuteSqlRaw(
            "ALTER TABLE DiaryEntries ADD COLUMN Tags TEXT NOT NULL DEFAULT ''"); }
        catch { /* 欄位已存在，忽略 */ }

        _diaryService = new DiaryService(_db);
        _fileService  = new FileService();

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        _calendarWindow = new FloatingCalendarWindow();
        _calendarWindow.SetSelectedDate(DateTime.Today);
        _calendarWindow.DateSelected += async date => await NavigateToDateAsync(date);

        // 初始化標籤輸入控件
        _tagInputBox = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2E)),
            MinWidth = 90,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
            ToolTip = "輸入後按 Enter 或逗號新增標籤"
        };
        _tagInputBox.KeyDown  += TagInputBox_KeyDown;
        _tagInputBox.GotFocus  += (_, _) => _tagInputPlaceholder.Visibility = Visibility.Collapsed;
        _tagInputBox.LostFocus += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_tagInputBox.Text))
                _tagInputPlaceholder.Visibility = Visibility.Visible;
            await CommitTagInputAsync();
        };

        _tagInputPlaceholder = new TextBlock
        {
            Text = "新增標籤...",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC4, 0xCC)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        _tagInputContainer = new Grid { MinWidth = 90 };
        _tagInputContainer.Children.Add(_tagInputBox);
        _tagInputContainer.Children.Add(_tagInputPlaceholder);

        Background = EditModeBg;

        UpdateCalendarButton(DateTime.Today);
        UpdateDateHeader(DateTime.Today);
        UpdatePlaceholderVisibility();
        RefreshTagsBar();

        Loaded += async (_, _) => await LoadEntryForDateAsync(_currentDate);
    }

    // ════════════════════════════════════════
    // 面板模式切換
    // ════════════════════════════════════════

    private async void ModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBrowseMode)
            await SwitchToEditModeAsync();
        else
            await SwitchToBrowseModeAsync();
    }

    internal async Task SwitchToBrowseModeAsync(string? searchKeyword = null)
    {
        await CommitTagInputAsync();
        _isBrowseMode = true;

        EditAreaBorder.Visibility     = Visibility.Collapsed;
        AttachmentArea.Visibility     = Visibility.Collapsed;
        BrowseScrollViewer.Visibility = Visibility.Visible;
        NavButtonsPanel.Visibility    = Visibility.Collapsed;
        ModeToggleButton.Content      = "✏️  編輯模式";

        Background = BrowseModeBg;
        BrowseScrollViewer.Background =
            new SolidColorBrush(Color.FromRgb(0xF5, 0xF8, 0xFF));

        await LoadBrowsePanelAsync(searchKeyword);
    }

    private async Task SwitchToEditModeAsync()
    {
        _isBrowseMode = false;

        EditAreaBorder.Visibility     = Visibility.Visible;
        AttachmentArea.Visibility     = Visibility.Visible;
        BrowseScrollViewer.Visibility = Visibility.Collapsed;
        NavButtonsPanel.Visibility    = Visibility.Visible;
        ModeToggleButton.Content      = "📋  瀏覽模式";

        Background = EditModeBg;

        await LoadEntryForDateAsync(_currentDate);
    }
}
