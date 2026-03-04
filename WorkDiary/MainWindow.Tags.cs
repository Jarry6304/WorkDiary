using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WorkDiary;

public partial class MainWindow
{
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
}
