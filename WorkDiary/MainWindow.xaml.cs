using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

    // ── 目前日期 ──
    private DateTime _currentDate = DateTime.Today;

    // ── 自動儲存防抖（2 秒無輸入後存入 SQLite）──
    private readonly DispatcherTimer _autoSaveTimer;

    // ── 浮動月曆 ──
    private readonly FloatingCalendarWindow _calendarWindow;

    // ── 每日折疊狀態（key = date.Date）──
    private readonly Dictionary<DateTime, bool> _collapsedDates = new();

    public MainWindow()
    {
        InitializeComponent();

        _db = new AppDbContext();
        _db.Database.EnsureCreated();

        // 相容舊 DB：新增 IsPinned 欄位（若已存在則略過）
        try { _db.Database.ExecuteSqlRaw(
            "ALTER TABLE DiaryEntries ADD COLUMN IsPinned INTEGER NOT NULL DEFAULT 0"); }
        catch { /* 欄位已存在，忽略 */ }

        _diaryService = new DiaryService(_db);
        _fileService  = new FileService();

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        _calendarWindow = new FloatingCalendarWindow();
        _calendarWindow.SetSelectedDate(DateTime.Today);
        _calendarWindow.DateSelected += async date => await NavigateToDateAsync(date);

        UpdateCalendarButton(DateTime.Today);
        UpdateDateHeader(DateTime.Today);
        UpdatePlaceholderVisibility();

        Loaded += async (_, _) => await LoadEntryForDateAsync(_currentDate);
    }

    // ════════════════════════════════════════
    // 日期導航
    // ════════════════════════════════════════

    private void CalendarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_calendarWindow.IsVisible)
        {
            _calendarWindow.Hide();
            return;
        }
        var pos = CalendarToggleButton.PointToScreen(
            new Point(0, CalendarToggleButton.ActualHeight + 4));
        _calendarWindow.Left = pos.X;
        _calendarWindow.Top  = pos.Y;
        _calendarWindow.Show();
    }

    private async void PrevDayButton_Click(object sender, RoutedEventArgs e)
        => await NavigateToDateAsync(_currentDate.AddDays(-1));

    private async void NextDayButton_Click(object sender, RoutedEventArgs e)
        => await NavigateToDateAsync(_currentDate.AddDays(1));

    private async Task NavigateToDateAsync(DateTime date)
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
        => CalendarToggleButton.Content = $"📅  {date:yyyy-MM-dd}";

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
    // 折疊 / 展開
    // ════════════════════════════════════════

    private void DateHeader_Toggle(object sender, MouseButtonEventArgs e)
    {
        var nowCollapsed = EditorGrid.Visibility == Visibility.Visible;
        _collapsedDates[_currentDate] = nowCollapsed;
        ApplyCollapseState(nowCollapsed);
    }

    private void ApplyCollapseState(bool collapsed)
    {
        if (collapsed)
        {
            EditorGrid.Visibility     = Visibility.Collapsed;
            WordCountBar.Visibility   = Visibility.Collapsed;
            AttachmentArea.Visibility = Visibility.Collapsed;
            EditorRow.Height          = GridLength.Auto;
            AttachmentRow.Height      = GridLength.Auto;
            CollapseChevron.Text      = "▶";
        }
        else
        {
            EditorGrid.Visibility     = Visibility.Visible;
            WordCountBar.Visibility   = Visibility.Visible;
            AttachmentArea.Visibility = Visibility.Visible;
            EditorRow.Height          = new GridLength(1, GridUnitType.Star);
            AttachmentRow.Height      = new GridLength(220, GridUnitType.Pixel);
            CollapseChevron.Text      = "▼";
        }
    }

    // ════════════════════════════════════════
    // 置頂（Pin）
    // ════════════════════════════════════════

    private async void PinButton_Click(object sender, RoutedEventArgs e)
    {
        var isPinned = PinButton.Tag as bool? ?? false;
        var newPinned = !isPinned;

        // 確保 DB 記錄存在才能更新 IsPinned
        var entry = await _diaryService.GetEntryAsync(_currentDate);
        if (entry == null)
            await _diaryService.GetOrCreateEntryAsync(_currentDate);

        await _diaryService.SetPinnedAsync(_currentDate, newPinned);
        UpdatePinButton(newPinned);
    }

    private void UpdatePinButton(bool pinned)
    {
        PinButton.Tag      = pinned;
        PinButton.Content  = pinned ? "★" : "☆";
        PinButton.Foreground = pinned
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00))  // 金色
            : new SolidColorBrush(Color.FromRgb(0xC0, 0xC4, 0xCC)); // 灰色
        PinButton.ToolTip = pinned ? "取消置頂" : "置頂此日記";
    }

    // ════════════════════════════════════════
    // 搜尋
    // ════════════════════════════════════════

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var keyword = SearchBox.Text;
        SearchPlaceholder.Visibility =
            string.IsNullOrEmpty(keyword) ? Visibility.Visible : Visibility.Collapsed;

        if (keyword.Length < 2)
        {
            SearchResultsPopup.IsOpen = false;
            return;
        }

        var results = await _diaryService.SearchAsync(keyword);
        if (results.Count == 0)
        {
            SearchResultsPopup.IsOpen = false;
            return;
        }

        SearchResultsListBox.ItemsSource = results
            .Select(r => new SearchResultItem
            {
                Date    = r.Date,
                IsPinned = r.IsPinned,
                Preview = ExtractPreview(r.Content, keyword)
            })
            .ToList();

        SearchResultsPopup.IsOpen = true;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = string.Empty;
            SearchResultsPopup.IsOpen = false;
        }
        else if (e.Key == Key.Down && SearchResultsPopup.IsOpen
                 && SearchResultsListBox.Items.Count > 0)
        {
            SearchResultsListBox.Focus();
            SearchResultsListBox.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        => SearchPlaceholder.Visibility = Visibility.Collapsed;

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
            SearchPlaceholder.Visibility = Visibility.Visible;
    }

    private async void SearchResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResultsListBox.SelectedItem is SearchResultItem result)
        {
            SearchResultsPopup.IsOpen = false;
            SearchBox.Text = string.Empty;
            SearchPlaceholder.Visibility = Visibility.Visible;
            await NavigateToDateAsync(result.Date);
        }
    }

    private static string ExtractPreview(string content, string keyword)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var idx = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return content.Length > 60 ? content[..60] + "…" : content;
        var start   = Math.Max(0, idx - 20);
        var end     = Math.Min(content.Length, idx + keyword.Length + 40);
        var snippet = content[start..end];
        if (start > 0)             snippet = "…" + snippet;
        if (end < content.Length)  snippet += "…";
        return snippet;
    }

    // ════════════════════════════════════════
    // DB 存取
    // ════════════════════════════════════════

    private async Task LoadEntryForDateAsync(DateTime date)
    {
        var entry = await _diaryService.GetEntryAsync(date);
        SetEditorText(entry?.Content ?? string.Empty);
        UpdatePinButton(entry?.IsPinned ?? false);

        // 還原此日期的折疊狀態
        var collapsed = _collapsedDates.TryGetValue(date, out var c) && c;
        ApplyCollapseState(collapsed);

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
        UpdateWordCount();
    }

    private void DiaryRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholderVisibility();
        UpdateWordCount();
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void UpdatePlaceholderVisibility()
    {
        EditorPlaceholder.Visibility = string.IsNullOrWhiteSpace(GetEditorText())
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWordCount()
    {
        var count = GetEditorText().Count(c => !char.IsWhiteSpace(c));
        WordCountText.Text = count == 0 ? string.Empty : $"{count} 字";
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
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
        => SetDropZoneHighlight(false);

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
    // 附件新增
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
                "格式不支援", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var relativePath = _fileService.CopyToStorage(sourcePath, _currentDate);
            var entry        = await _diaryService.GetOrCreateEntryAsync(_currentDate);

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

            await RefreshAttachmentListAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"附件加入失敗：{ex.Message}",
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

    // ════════════════════════════════════════
    // 附件操作（開啟 / 顯示 / 刪除）
    // ════════════════════════════════════════

    private void AttachmentListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AttachmentListView.SelectedItem is AttachmentItem item)
            OpenAttachment(item);
    }

    private void AttachmentMenu_Open(object sender, RoutedEventArgs e)
    {
        if (AttachmentListView.SelectedItem is AttachmentItem item)
            OpenAttachment(item);
    }

    private void AttachmentMenu_ShowInFolder(object sender, RoutedEventArgs e)
    {
        if (AttachmentListView.SelectedItem is AttachmentItem item)
        {
            var fullPath = _fileService.GetFullPath(item.RelativePath);
            if (File.Exists(fullPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
                    { UseShellExecute = true });
        }
    }

    private async void AttachmentMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (AttachmentListView.SelectedItem is AttachmentItem item)
            await DeleteAttachmentAsync(item);
    }

    private async void AttachmentListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && AttachmentListView.SelectedItem is AttachmentItem item)
            await DeleteAttachmentAsync(item);
    }

    private void OpenAttachment(AttachmentItem item)
    {
        var fullPath = _fileService.GetFullPath(item.RelativePath);
        if (File.Exists(fullPath))
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
    }

    private async Task DeleteAttachmentAsync(AttachmentItem item)
    {
        var result = MessageBox.Show(
            $"確定要刪除附件「{item.FileName}」？\n此操作將同時刪除儲存的檔案，無法復原。",
            "刪除確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        await _diaryService.DeleteAttachmentAsync(item.DbId);
        _fileService.DeleteFile(item.RelativePath);
        await RefreshAttachmentListAsync();
    }

    // ════════════════════════════════════════
    // 顯示模型對應 / 工具方法
    // ════════════════════════════════════════

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
        ".xlsx" or ".xls"           => "📊",
        ".pdf"                      => "📕",
        ".pptx" or ".ppt"           => "📙",
        ".txt"                      => "📝",
        ".jpg" or ".jpeg" or ".png" => "🖼️",
        _                           => "📄"
    };

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1_024     => $"{bytes} B",
        < 1_048_576 => $"{bytes / 1024.0:F1} KB",
        _           => $"{bytes / 1_048_576.0:F1} MB"
    };
}

// ── UI 顯示模型 ──
public class AttachmentItem
{
    public int    DbId         { get; set; }
    public string FileName     { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FileSize     { get; set; } = string.Empty;
    public string Icon         { get; set; } = "📄";
}

public class SearchResultItem
{
    public DateTime Date     { get; init; }
    public bool     IsPinned { get; init; }
    public string   Preview  { get; init; } = string.Empty;

    public string DateLabel
    {
        get
        {
            var pin  = IsPinned ? " ★" : string.Empty;
            var today = Date.Date == DateTime.Today ? "（今天）" : string.Empty;
            return $"{Date:yyyy-MM-dd}{pin}  {today}";
        }
    }
}
