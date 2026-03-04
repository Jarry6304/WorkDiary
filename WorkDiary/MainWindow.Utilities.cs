using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkDiary.Models;

namespace WorkDiary;

// ── 顯示模型 ──

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
            var pin   = IsPinned ? " ★" : string.Empty;
            var today = Date.Date == DateTime.Today ? "（今天）" : string.Empty;
            return $"{Date:yyyy-MM-dd}{pin}  {today}";
        }
    }
}

// ── 工具方法 ──

public partial class MainWindow
{
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

    internal static string GetChineseDayOfWeek(DayOfWeek day) => day switch
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

    internal static string FormatBrowseDateLabel(DiaryEntry entry)
    {
        var day   = GetChineseDayOfWeek(entry.Date.DayOfWeek);
        var today = entry.Date.Date == DateTime.Today ? "　（今天）" : string.Empty;
        return $"{entry.Date:yyyy年M月d日}　{day}{today}";
    }

    internal static WrapPanel BuildTagChipsPanel(string tagsStr, double fontSize)
    {
        var panel = new WrapPanel();
        if (string.IsNullOrWhiteSpace(tagsStr)) return panel;

        foreach (var tag in tagsStr.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0xD9, 0xF5)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            chip.Child = new TextBlock
            {
                Text = tag,
                FontSize = fontSize,
                FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
            };
            panel.Children.Add(chip);
        }
        return panel;
    }

    internal static bool IsDescendantOf(DependencyObject? child, DependencyObject parent)
    {
        while (child != null)
        {
            if (child == parent) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }
}
