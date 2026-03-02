using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WorkDiary.Data;
using WorkDiary.Models;
using WorkDiary.Services;

namespace WorkDiary;

public partial class MainWindow : Window
{
    // ── 服務層 ──
    private readonly AppDbContext  _db;
    private readonly DiaryService  _diaryService;
    private readonly FileService   _fileService;

    // ── 目前顯示的日期 ──
    private DateTime _currentDate = DateTime.Today;

    // ── 自動儲存防抖（2 秒無輸入後存入 SQLite）──
    private readonly DispatcherTimer _autoSaveTimer;

    // ── 浮動月曆視窗 ──
    private readonly FloatingCalendarWindow _calendarWindow;

    public MainWindow()
    {
        InitializeComponent();

        // 初始化 SQLite（首次執行自動建表）
        _db = new AppDbContext();
        _db.Database.EnsureCreated();
        _diaryService = new DiaryService(_db);
        _fileService  = new FileService();

        // 自動儲存計時器
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        // 浮動月曆
        _calendarWindow = new FloatingCalendarWindow();
        _calendarWindow.SetSelectedDate(DateTime.Today);
        _calendarWindow.DateSelected += OnCalendarDateSelected;

        UpdateCalendarButton(DateTime.Today);
        UpdateDateHeader(DateTime.Today);
        UpdatePlaceholderVisibility();

        // 視窗顯示後載入今日記錄
        Loaded += async (_, _) => await LoadEntryForDateAsync(_currentDate);
    }

    // ════════════════════════════════════════
    // 月曆切換按鈕
    // ════════════════════════════════════════

    private void CalendarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_calendarWindow.IsVisible)
        {
            _calendarWindow.Hide();
            return;
        }

        var btnPos = CalendarToggleButton.PointToScreen(
            new Point(0, CalendarToggleButton.ActualHeight + 4));
        _calendarWindow.Left = btnPos.X;
        _calendarWindow.Top  = btnPos.Y;
        _calendarWindow.Show();
    }

    // 浮動月曆選取日期後的回呼（async void：WPF 事件處理允許）
    private async void OnCalendarDateSelected(DateTime date)
    {
        _autoSaveTimer.Stop();
        await _diaryService.SaveContentAsync(_currentDate, GetEditorText());

        _currentDate = date;
        _calendarWindow.SetSelectedDate(date);
        UpdateCalendarButton(date);
        UpdateDateHeader(date);
        await LoadEntryForDateAsync(date);
    }

    private void UpdateCalendarButton(DateTime date)
    {
        CalendarToggleButton.Content = $"📅  {date:yyyy-MM-dd}";
    }

    // ════════════════════════════════════════
    // 日期標題
    // ════════════════════════════════════════

    private void UpdateDateHeader(DateTime date)
    {
        var dayLabel = GetChineseDayOfWeek(date.DayOfWeek);
        DateHeaderText.Text = date.Date == DateTime.Today
            ? $"{date:yyyy年M月d日}　{dayLabel}　（今天）"
            : $"{date:yyyy年M月d日}　{dayLabel}";
    }

    private static string GetChineseDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday    => "星期一",
        DayOfWeek.Tuesday   => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday  => "星期四",
        DayOfWeek.Friday    => "星期五",
        DayOfWeek.Saturday  => "星期六",
        DayOfWeek.Sunday    => "星期日",
        _                   => string.Empty
    };

    // ════════════════════════════════════════
    // DB 存取
    // ════════════════════════════════════════

    private async Task LoadEntryForDateAsync(DateTime date)
    {
        var entry = await _diaryService.GetEntryAsync(date);
        SetEditorText(entry?.Content ?? string.Empty);

        if (entry?.Attachments is { Count: > 0 })
        {
            AttachmentListView.ItemsSource = entry.Attachments
                .Select(MapToDisplayItem)
                .ToList();
            ShowAttachmentList();
        }
        else
        {
            AttachmentListView.ItemsSource = null;
            ShowDropHint();
        }
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        await _diaryService.SaveContentAsync(_currentDate, GetEditorText());
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _autoSaveTimer.Stop();
        await _diaryService.SaveContentAsync(_currentDate, GetEditorText());
        _calendarWindow.Close();
        _db.Dispose();
        base.OnClosing(e);
    }

    // ════════════════════════════════════════
    // RichTextBox 輔助
    // ════════════════════════════════════════

    private string GetEditorText()
    {
        var range = new TextRange(
            DiaryRichTextBox.Document.ContentStart,
            DiaryRichTextBox.Document.ContentEnd);
        return range.Text.Trim();
    }

    private void SetEditorText(string text)
    {
        DiaryRichTextBox.Document.Blocks.Clear();
        if (!string.IsNullOrEmpty(text))
            DiaryRichTextBox.Document.Blocks.Add(new Paragraph(new Run(text)));
        UpdatePlaceholderVisibility();
    }

    private void DiaryRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholderVisibility();
        // 重置防抖計時器
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void UpdatePlaceholderVisibility()
    {
        EditorPlaceholder.Visibility = string.IsNullOrWhiteSpace(GetEditorText())
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ════════════════════════════════════════
    // 附件 UI 切換
    // ════════════════════════════════════════

    private void ShowDropHint()
    {
        DropHint.Visibility           = Visibility.Visible;
        AttachmentListView.Visibility = Visibility.Collapsed;
    }

    private void ShowAttachmentList()
    {
        DropHint.Visibility           = Visibility.Collapsed;
        AttachmentListView.Visibility = Visibility.Visible;
    }

    // ════════════════════════════════════════
    // 拖曳事件
    // ════════════════════════════════════════

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            SetDropZoneHighlight(true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        SetDropZoneHighlight(false);
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        SetDropZoneHighlight(false);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files)
                await AddFileAsync(file);
        }
    }

    private void SetDropZoneHighlight(bool active)
    {
        DropZoneBorder.Background = active
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF))
            : Brushes.Transparent;
        DropZoneBorder.BorderBrush = active
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
            : Brushes.Transparent;
    }

    // ════════════════════════════════════════
    // 附件處理
    // ════════════════════════════════════════

    private static readonly HashSet<string> SupportedExtensions =
        new() { ".xlsx", ".xls", ".pdf", ".pptx", ".ppt", ".txt", ".jpg", ".jpeg", ".png" };

    private async Task AddFileAsync(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext))
        {
            MessageBox.Show(
                $"不支援的檔案格式：{ext}\n\n支援格式：Excel、PDF、PPT、TXT、JPG、PNG",
                "格式不支援",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            // 複製到本地儲存資料夾
            var relativePath = _fileService.CopyToStorage(sourcePath, _currentDate);

            // 取得或建立 DB 記錄
            var entry = await _diaryService.GetOrCreateEntryAsync(_currentDate);

            // 避免重複（同 relativePath）
            if (entry.Attachments.Any(a => a.RelativePath == relativePath))
                return;

            var info = new FileInfo(sourcePath);
            await _diaryService.AddAttachmentAsync(new FileAttachment
            {
                DiaryEntryId  = entry.Id,
                FileName      = Path.GetFileName(sourcePath),
                RelativePath  = relativePath,
                Extension     = ext,
                FileSizeBytes = info.Exists ? info.Length : 0,
                AddedAt       = DateTime.Now
            });

            // 重新從 DB 載入清單（確保包含最新記錄）
            await RefreshAttachmentListAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"附件加入失敗：{ex.Message}",
                "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshAttachmentListAsync()
    {
        var entry = await _diaryService.GetEntryAsync(_currentDate);
        if (entry?.Attachments is { Count: > 0 })
        {
            AttachmentListView.ItemsSource = entry.Attachments
                .Select(MapToDisplayItem)
                .ToList();
            ShowAttachmentList();
        }
        else
        {
            AttachmentListView.ItemsSource = null;
            ShowDropHint();
        }
    }

    // 雙擊附件 → 系統預設程式開啟
    private void AttachmentListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AttachmentListView.SelectedItem is AttachmentItem item)
        {
            var fullPath = _fileService.GetFullPath(item.RelativePath);
            if (File.Exists(fullPath))
                Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
    }

    private AttachmentItem MapToDisplayItem(FileAttachment f) => new()
    {
        DbId         = f.Id,
        FileName     = f.FileName,
        RelativePath = f.RelativePath,
        Icon         = GetFileIcon(f.Extension),
        FileSize     = FormatFileSize(f.FileSizeBytes)
    };

    private static string GetFileIcon(string ext) => ext switch
    {
        ".xlsx" or ".xls"            => "📊",
        ".pdf"                       => "📕",
        ".pptx" or ".ppt"            => "📙",
        ".txt"                       => "📝",
        ".jpg" or ".jpeg" or ".png"  => "🖼️",
        _                            => "📄"
    };

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1_024         => $"{bytes} B",
        < 1_048_576     => $"{bytes / 1024.0:F1} KB",
        _               => $"{bytes / 1_048_576.0:F1} MB"
    };
}

// ── UI 顯示模型（與 DB 模型 FileAttachment 分離）──
public class AttachmentItem
{
    public int    DbId         { get; set; }
    public string FileName     { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FileSize     { get; set; } = string.Empty;
    public string Icon         { get; set; } = "📄";
}
