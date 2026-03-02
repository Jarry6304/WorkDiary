using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WorkDiary;

public partial class MainWindow : Window
{
    // ── 暫存資料（Phase 1 純記憶體；Phase 2 改為 SQLite）──
    private readonly Dictionary<DateTime, string> _textEntries = new();
    private readonly Dictionary<DateTime, List<AttachmentItem>> _attachmentEntries = new();
    private DateTime _currentDate = DateTime.Today;

    // ── 浮動月曆視窗（建立一次，重複顯示/隱藏）──
    private readonly FloatingCalendarWindow _calendarWindow;

    public MainWindow()
    {
        InitializeComponent();

        _calendarWindow = new FloatingCalendarWindow();
        _calendarWindow.SetSelectedDate(DateTime.Today);
        _calendarWindow.DateSelected += OnCalendarDateSelected;

        UpdateCalendarButton(DateTime.Today);
        UpdateDateHeader(DateTime.Today);
        UpdatePlaceholderVisibility();
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

        // 定位浮動視窗：出現在按鈕正下方
        var btnPos = CalendarToggleButton.PointToScreen(
            new Point(0, CalendarToggleButton.ActualHeight + 4));

        _calendarWindow.Left = btnPos.X;
        _calendarWindow.Top  = btnPos.Y;
        _calendarWindow.Show();
    }

    // ── 浮動月曆選取日期回呼 ──
    private void OnCalendarDateSelected(DateTime date)
    {
        SaveCurrentEntry();
        _currentDate = date;
        LoadEntryForDate(_currentDate);
        UpdateDateHeader(_currentDate);
        UpdateCalendarButton(_currentDate);
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
        var isToday  = date.Date == DateTime.Today;
        var dayLabel = GetChineseDayOfWeek(date.DayOfWeek);
        DateHeaderText.Text = isToday
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
    // 記憶體存取
    // ════════════════════════════════════════

    private void SaveCurrentEntry()
    {
        _textEntries[_currentDate] = GetEditorText();
    }

    private void LoadEntryForDate(DateTime date)
    {
        SetEditorText(_textEntries.GetValueOrDefault(date, string.Empty));

        var attachments = _attachmentEntries.GetValueOrDefault(date);
        if (attachments is { Count: > 0 })
        {
            AttachmentListView.ItemsSource = new List<AttachmentItem>(attachments);
            ShowAttachmentList();
        }
        else
        {
            AttachmentListView.ItemsSource = null;
            ShowDropHint();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveCurrentEntry();
        _calendarWindow.Close();
        base.OnClosing(e);
    }

    // ════════════════════════════════════════
    // RichTextBox 輔助方法
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
    }

    private void UpdatePlaceholderVisibility()
    {
        EditorPlaceholder.Visibility = string.IsNullOrWhiteSpace(GetEditorText())
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ════════════════════════════════════════
    // 附件清單顯示切換
    // ════════════════════════════════════════

    private void ShowDropHint()
    {
        DropHint.Visibility = Visibility.Visible;
        AttachmentListView.Visibility = Visibility.Collapsed;
    }

    private void ShowAttachmentList()
    {
        DropHint.Visibility = Visibility.Collapsed;
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

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        SetDropZoneHighlight(false);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files)
                AddFileToList(file);
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

    private void AddFileToList(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext))
        {
            MessageBox.Show(
                $"不支援的檔案格式：{ext}\n\n支援格式：Excel、PDF、PPT、TXT、JPG、PNG",
                "格式不支援",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!_attachmentEntries.ContainsKey(_currentDate))
            _attachmentEntries[_currentDate] = new List<AttachmentItem>();

        var existing = _attachmentEntries[_currentDate];
        if (existing.Exists(a => a.FilePath == filePath))
            return;

        existing.Add(new AttachmentItem
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            Icon     = GetFileIcon(ext),
            FileSize = GetFileSizeText(filePath)
        });

        AttachmentListView.ItemsSource = new List<AttachmentItem>(existing);
        ShowAttachmentList();
    }

    // 雙擊附件 → 系統預設程式開啟
    private void AttachmentListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AttachmentListView.SelectedItem is AttachmentItem item && File.Exists(item.FilePath))
            Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
    }

    private static string GetFileIcon(string ext) => ext switch
    {
        ".xlsx" or ".xls"            => "📊",
        ".pdf"                       => "📕",
        ".pptx" or ".ppt"            => "📙",
        ".txt"                       => "📝",
        ".jpg" or ".jpeg" or ".png"  => "🖼️",
        _                            => "📄"
    };

    private static string GetFileSizeText(string filePath)
    {
        try
        {
            var size = new FileInfo(filePath).Length;
            return size switch
            {
                < 1_024         => $"{size} B",
                < 1_048_576     => $"{size / 1024.0:F1} KB",
                _               => $"{size / 1_048_576.0:F1} MB"
            };
        }
        catch { return string.Empty; }
    }
}

// ── 附件資料模型（Phase 2 搬至 Models 資料夾並加入 DB 欄位）──
public class AttachmentItem
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileSize { get; set; } = string.Empty;
    public string Icon     { get; set; } = "📄";
}
