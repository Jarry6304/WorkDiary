using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkDiary.Models;

namespace WorkDiary;

public partial class MainWindow
{
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

        var tagChips = BuildTagChipsPanel(entry.Tags, 10.5);
        tagChips.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(tagChips, Dock.Left);
        dock.Children.Add(tagChips);

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

        // 標籤 chip 詳細區
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

        // 附件預覽區
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

        // 展開/折疊事件
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

        // 置頂按鈕事件
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

    // ── 附件預覽（含非同步縮圖載入）──

    private StackPanel BuildAttachmentsSection(IEnumerable<FileAttachment> attachments)
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "📎  附件",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2E)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // 圖片縮圖區（非同步載入，先佔位再填入）
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

                // 先建立空 Image 控件（placeholder），不阻塞 UI
                var imgCtrl = new System.Windows.Controls.Image
                {
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

                // 背景讀檔，Background 優先度解碼，避免 UI 凍結
                var path = fullPath;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(path);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.StreamSource = new MemoryStream(bytes);
                                bmp.DecodePixelWidth = 80;
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                imgCtrl.Source = bmp;
                            }
                            catch { /* 損壞圖片，保留空白 */ }
                        }, DispatcherPriority.Background);
                    }
                    catch { /* 檔案讀取失敗，略過 */ }
                });
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

            var iconText = new TextBlock
            {
                Text = GetFileIcon(att.Extension),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(iconText, Dock.Left);
            rowDock.Children.Add(iconText);

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
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
    }
}
