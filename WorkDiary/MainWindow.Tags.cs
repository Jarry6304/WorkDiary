using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WorkDiary;

public partial class MainWindow
{
    // ════════════════════════════════════════
    // 標籤列（編輯模式）
    // ════════════════════════════════════════

    // ── 自動完成 Popup ──
    private Popup    _tagSuggestionPopup = null!;
    private ListBox  _tagSuggestionList  = null!;
    private bool     _pickingSuggestion;

    internal void InitTagAutocompletePopup()
    {
        _tagSuggestionList = new ListBox
        {
            MaxHeight   = 160,
            FontFamily  = new FontFamily("Microsoft JhengHei UI, Segoe UI"),
            FontSize    = 11,
            Background  = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        _tagSuggestionList.MouseLeftButtonUp += TagSuggestion_MouseUp;

        _tagSuggestionPopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen          = false,
            Placement          = PlacementMode.Bottom,
            PlacementTarget    = _tagInputContainer,
            Child = new Border
            {
                Background      = Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                Child           = _tagSuggestionList
            }
        };

        _tagInputBox.TextChanged += TagInputBox_TextChanged;
    }

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
            _tagSuggestionPopup.IsOpen = false;
            await CommitTagInputAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _tagSuggestionPopup.IsOpen = false;
        }
    }

    private async void TagInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = _tagInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            _tagSuggestionPopup.IsOpen = false;
            return;
        }

        var allTags = await _diaryService.GetAllTagNamesAsync();
        var suggestions = allTags
            .Where(t => t.StartsWith(text, StringComparison.OrdinalIgnoreCase)
                     && !_currentTags.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        _tagSuggestionList.ItemsSource = suggestions;
        _tagSuggestionPopup.IsOpen = suggestions.Count > 0;
    }

    private async void TagSuggestion_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_tagSuggestionList.SelectedItem is string selectedTag)
        {
            _pickingSuggestion = true;
            _tagSuggestionPopup.IsOpen = false;
            _tagInputBox.Text = selectedTag;
            await CommitTagInputAsync();
            _pickingSuggestion = false;
            _tagInputBox.Focus();
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
        await _diaryService.SyncTagsAsync(_currentDate, _currentTags);
    }
}
