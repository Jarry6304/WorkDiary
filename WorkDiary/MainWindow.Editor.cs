using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace WorkDiary;

public partial class MainWindow
{
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
        var isPinned  = PinButton.Tag as bool? ?? false;
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

        _currentTags = string.IsNullOrWhiteSpace(entry?.Tags)
            ? new List<string>()
            : entry.Tags.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        RefreshTagsBar();

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
}
