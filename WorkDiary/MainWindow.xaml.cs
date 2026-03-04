using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    // ── 面板模式 ──
    private bool _isBrowseMode = false;

    // ── 模式底色 ──
    private static readonly SolidColorBrush EditModeBg =
        new(Color.FromRgb(0xEB, 0xF9, 0xEF));   // 淡綠 #EBF9EF
    private static readonly SolidColorBrush BrowseModeBg =
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
        _tagInputBox.KeyDown += TagInputBox_KeyDown;
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

        // 初始為編輯模式底色
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

    private async Task SwitchToBrowseModeAsync(string? searchKeyword = null)
    {
        await CommitTagInputAsync();
        _isBrowseMode = true;

        EditAreaBorder.Visibility  = Visibility.Collapsed;
        AttachmentArea.Visibility  = Visibility.Collapsed;
        BrowseScrollViewer.Visibility = Visibility.Visible;
        NavButtonsPanel.Visibility = Visibility.Collapsed;
        ModeToggleButton.Content   = "✏️  編輯模式";

        // 瀏覽模式底色：淡藍
        Background = BrowseModeBg;
        BrowseScrollViewer.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF8, 0xFF));

        await LoadBrowsePanelAsync(searchKeyword);
    }

    private async Task SwitchToEditModeAsync()
    {
        _isBrowseMode = false;

        EditAreaBorder.Visibility  = Visibility.Visible;
        AttachmentArea.Visibility  = Visibility.Visible;
        BrowseScrollViewer.Visibility = Visibility.Collapsed;
        NavButtonsPanel.Visibility = Visibility.Visible;
        ModeToggleButton.Content   = "📋  瀏覽模式";

        // 編輯模式底色：淡綠
        Background = EditModeBg;

        await LoadEntryForDateAsync(_currentDate);
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
                Date     = r.Date,
                IsPinned = r.IsPinned,
                Preview  = ExtractPreview(r.Content, keyword)
            })
            .ToList();

        SearchResultsPopup.IsOpen = true;
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Enter → 全文搜尋並切換至瀏覽模式
            await ExecuteFullSearchAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
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

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
        => await ExecuteFullSearchAsync();

    private async Task ExecuteFullSearchAsync()
    {
        var keyword = SearchBox.Text.Trim();
        SearchResultsPopup.IsOpen = false;
        // 切換到瀏覽模式，帶搜尋關鍵字
        await SwitchToBrowseModeAsync(string.IsNullOrWhiteSpace(keyword) ? null : keyword);
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

            // 若在瀏覽模式，先切回編輯模式
            if (_isBrowseMode)
                await SwitchToEditModeAsync();

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
        if (start > 0)            snippet = "…" + snippet;
        if (end < content.Length) snippet += "…";
        return snippet;
    }

    // ════════════════════════════════════════
    // 瀏覽面板
    // ════════════════════════════════════════

    private async Task LoadBrowsePanelAsync(string? keyword = null)
    {
        BrowseEntriesPanel.Children.Clear();

        List<DiaryEntry> entries;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            entries = await _diaryService.GetAllEntriesAsync();
        }
        else
        {
            entries = await _diaryService.SearchForBrowseAsync(keyword);

            // 顯示搜尋標題列
            BrowseEntriesPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0xD9, 0xF5)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8),
                Child = new TextBlock
                {
                    Text = $"🔍  搜尋「{keyword}」：共 {entries.Count} 筆結果",
                    FontSize = 13,
                    FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x5A, 0x9E))
                }
            });
        }

        if (entries.Count == 0)
        {
            BrowseEntriesPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(keyword) ? "尚無日誌記錄" : "找不到符合的日誌",
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA3, 0xA8)),
                Margin = new Thickness(0, 48, 0, 0),
                FontSize = 14,
                FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI")
            });
            return;
        }

        foreach (var entry in entries)
            BrowseEntriesPanel.Children.Add(CreateEntryRow(entry));
    }

    private UIElement CreateEntryRow(DiaryEntry entry)
    {
        var outer = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE4, 0xE8)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var outerStack = new StackPanel();
        outer.Child = outerStack;

        // ── 標題列 ──
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Hand
        };

        var dock = new DockPanel { LastChildFill = false };
        headerBorder.Child = dock;

        // 折疊箭頭
        var chevron = new TextBlock
        {
            Text = "▶",
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB5, 0xBC)),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(chevron, Dock.Left);
        dock.Children.Add(chevron);

        // 置頂按鈕
        var pinBtn = new Button
        {
            Content = entry.IsPinned ? "★" : "☆",
            Style = (Style)FindResource("GhostButton"),
            FontSize = 14,
            Foreground = entry.IsPinned
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00))
                : new SolidColorBrush(Color.FromRgb(0xC0, 0xC4, 0xCC)),
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = entry.IsPinned ? "取消置頂" : "置頂此日記"
        };
        DockPanel.SetDock(pinBtn, Dock.Left);
        dock.Children.Add(pinBtn);

        // 日期文字
        var dateText = new TextBlock
        {
            Text = FormatBrowseDateLabel(entry),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2E)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(dateText, Dock.Left);
        dock.Children.Add(dateText);

        // 標籤 chips（0.75x = 14×0.75 = 10.5）
        var tagChips = BuildTagChipsPanel(entry.Tags, 10.5);
        tagChips.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(tagChips, Dock.Left);
        dock.Children.Add(tagChips);

        // 附件數量提示（標題列右側）
        if (entry.Attachments is { Count: > 0 })
        {
            var attachHint = new TextBlock
            {
                Text = $"📎 {entry.Attachments.Count}",
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA3, 0xA8)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(attachHint, Dock.Left);
            dock.Children.Add(attachHint);
        }

        // ── 展開內容區 ──
        var contentBorder = new Border
        {
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(16, 10, 16, 14),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE4, 0xE8)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = Brushes.White
        };

        var contentStack = new StackPanel();
        contentBorder.Child = contentStack;

        // Labels 區塊（副擋，可點擊樣式）
        if (!string.IsNullOrWhiteSpace(entry.Tags))
        {
            var labelsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var tag in entry.Tags.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 6, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = $"標籤：{tag}"
                };
                chip.Child = new TextBlock
                {
                    Text = tag,
                    FontSize = 12,
                    FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
                };
                labelsPanel.Children.Add(chip);
            }
            contentStack.Children.Add(labelsPanel);
        }

        // 日誌文字
        contentStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Content) ? "（無內容）" : entry.Content,
            FontSize = 13,
            FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
            Foreground = string.IsNullOrWhiteSpace(entry.Content)
                ? new SolidColorBrush(Color.FromRgb(0xC0, 0xC4, 0xCC))
                : new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2E)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Margin = new Thickness(0, 0, 0, entry.Attachments?.Count > 0 ? 12 : 0)
        });

        // ── 附件預覽區 ──
        if (entry.Attachments is { Count: > 0 })
        {
            contentStack.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE4, 0xE8)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 0),
                Child = BuildAttachmentsSection(entry.Attachments)
            });
        }

        outerStack.Children.Add(headerBorder);
        outerStack.Children.Add(contentBorder);

        // ── 展開/折疊事件 ──
        headerBorder.MouseLeftButtonUp += (_, e) =>
        {
            if (IsDescendantOf(e.OriginalSource as DependencyObject, pinBtn)) return;

            var expanding = contentBorder.Visibility == Visibility.Collapsed;
            contentBorder.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = expanding ? "▼" : "▶";
            headerBorder.Background = expanding
                ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF8, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        };

        // ── 置頂按鈕事件 ──
        pinBtn.Click += async (_, _) =>
        {
            var newPinned = !entry.IsPinned;
            var dbEntry = await _diaryService.GetEntryAsync(entry.Date);
            if (dbEntry == null) await _diaryService.GetOrCreateEntryAsync(entry.Date);
            await _diaryService.SetPinnedAsync(entry.Date, newPinned);
            entry.IsPinned = newPinned;
            await LoadBrowsePanelAsync();
        };

        return outer;
    }

    private StackPanel BuildAttachmentsSection(IEnumerable<FileAttachment> attachments)
    {
        var panel = new StackPanel();

        // 附件區標題
        panel.Children.Add(new TextBlock
        {
            Text = "📎  附件",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2E)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // 圖片縮圖列（先收集圖片）
        var images = attachments
            .Where(a => a.Extension is ".jpg" or ".jpeg" or ".png")
            .ToList();

        if (images.Count > 0)
        {
            var imgPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var img in images)
            {
                var fullPath = _fileService.GetFullPath(img.RelativePath);
                if (!File.Exists(fullPath)) continue;

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(fullPath);
                    bmp.DecodePixelWidth = 80;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    var imgCtrl = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Width = 80,
                        Height = 60,
                        Stretch = Stretch.UniformToFill,
                        Margin = new Thickness(0, 0, 6, 4),
                        Cursor = Cursors.Hand,
                        ToolTip = img.FileName,
                        Clip = new RectangleGeometry(new Rect(0, 0, 80, 60), 4, 4)
                    };
                    var captured = img;
                    imgCtrl.MouseLeftButtonUp += (_, _) => OpenFileAttachment(captured);
                    imgPanel.Children.Add(imgCtrl);
                }
                catch { /* 圖片載入失敗，跳過 */ }
            }
            if (imgPanel.Children.Count > 0)
                panel.Children.Add(imgPanel);
        }

        // 非圖片附件清單
        foreach (var att in attachments)
        {
            var captured = att;
            var row = new Border
            {
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            row.MouseEnter += (_, _) =>
                row.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
            row.MouseLeave += (_, _) =>
                row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => OpenFileAttachment(captured);

            var rowDock = new DockPanel { LastChildFill = true };
            rowDock.Children.Add(new TextBlock
            {
                Text = GetFileIcon(att.Extension),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            DockPanel.SetDock(rowDock.Children[0], Dock.Left);

            var sizeText = new TextBlock
            {
                Text = FormatFileSize(att.FileSizeBytes),
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA3, 0xA8)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(sizeText, Dock.Right);
            rowDock.Children.Add(sizeText);

            rowDock.Children.Add(new TextBlock
            {
                Text = att.FileName,
                FontSize = 13,
                FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2E)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            row.Child = rowDock;
            panel.Children.Add(row);
        }

        return panel;
    }

    private void OpenFileAttachment(FileAttachment att)
    {
        var fullPath = _fileService.GetFullPath(att.RelativePath);
        if (File.Exists(fullPath))
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
    }

    private static WrapPanel BuildTagChipsPanel(string tagsStr, double fontSize)
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

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject parent)
    {
        while (child != null)
        {
            if (child == parent) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private string FormatBrowseDateLabel(DiaryEntry entry)
    {
        var day = GetChineseDayOfWeek(entry.Date.DayOfWeek);
        var today = entry.Date.Date == DateTime.Today ? "　（今天）" : string.Empty;
        return $"{entry.Date:yyyy年M月d日}　{day}{today}";
    }

    // ════════════════════════════════════════
    // 標籤列（編輯模式）
    // ════════════════════════════════════════

    private void RefreshTagsBar()
    {
        TagsWrapPanel.Children.Clear();

        TagsWrapPanel.Children.Add(new TextBlock
        {
            Text = "標籤：",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA3, 0xA8)),
            FontSize = 12,
            FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });

        foreach (var tag in _currentTags.ToList())
        {
            var captured = tag;
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6, 2, 2, 2),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
            chipContent.Children.Add(new TextBlock
            {
                Text = captured,
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0)
            });

            var removeBtn = new Button
            {
                Content = "×",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                FontSize = 12,
                Padding = new Thickness(2, 0, 3, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            removeBtn.Click += async (_, _) =>
            {
                _currentTags.Remove(captured);
                await SaveCurrentTagsAsync();
                RefreshTagsBar();
            };
            chipContent.Children.Add(removeBtn);
            chip.Child = chipContent;
            TagsWrapPanel.Children.Add(chip);
        }

        TagsWrapPanel.Children.Add(_tagInputContainer);
        _tagInputPlaceholder.Visibility = string.IsNullOrEmpty(_tagInputBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void TagInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.OemComma)
        {
            await CommitTagInputAsync();
            e.Handled = true;
        }
    }

    private async Task CommitTagInputAsync()
    {
        var text = _tagInputBox.Text.Trim().TrimEnd(',');
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!_currentTags.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            _currentTags.Add(text);
            await SaveCurrentTagsAsync();
        }
        _tagInputBox.Text = string.Empty;
        _tagInputPlaceholder.Visibility = Visibility.Visible;
        RefreshTagsBar();
    }

    private async Task SaveCurrentTagsAsync()
    {
        var tagsStr = string.Join(",", _currentTags);
        var entry = await _diaryService.GetEntryAsync(_currentDate);
        if (entry == null && _currentTags.Count > 0)
            await _diaryService.GetOrCreateEntryAsync(_currentDate);
        await _diaryService.SaveTagsAsync(_currentDate, tagsStr);
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
        await CommitTagInputAsync();
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
            TagsBar.Visibility        = Visibility.Collapsed;
            EditorRow.Height          = GridLength.Auto;
            AttachmentRow.Height      = GridLength.Auto;
            CollapseChevron.Text      = "▶";
        }
        else
        {
            EditorGrid.Visibility     = Visibility.Visible;
            WordCountBar.Visibility   = Visibility.Visible;
            AttachmentArea.Visibility = Visibility.Visible;
            TagsBar.Visibility        = Visibility.Visible;
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
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00))
            : new SolidColorBrush(Color.FromRgb(0xC0, 0xC4, 0xCC));
        PinButton.ToolTip = pinned ? "取消置頂" : "置頂此日記";
    }

    // ════════════════════════════════════════
    // DB 存取
    // ════════════════════════════════════════

    private async Task LoadEntryForDateAsync(DateTime date)
    {
        var entry = await _diaryService.GetEntryAsync(date);
        SetEditorText(entry?.Content ?? string.Empty);
        UpdatePinButton(entry?.IsPinned ?? false);

        // 載入標籤
        _currentTags = string.IsNullOrWhiteSpace(entry?.Tags)
            ? new List<string>()
            : entry.Tags.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        RefreshTagsBar();

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
        await CommitTagInputAsync();
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
            var pin   = IsPinned ? " ★" : string.Empty;
            var today = Date.Date == DateTime.Today ? "（今天）" : string.Empty;
            return $"{Date:yyyy-MM-dd}{pin}  {today}";
        }
    }
}
