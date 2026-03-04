using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WorkDiary;

public partial class MainWindow
{
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
}
