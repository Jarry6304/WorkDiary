using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkDiary.Models;

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

        var results = await HybridSearchAsync(keyword, takeLimit: 10);
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
            var keyword = SearchBox.Text;
            SearchResultsPopup.IsOpen = false;
            SearchBox.Text = string.Empty;
            SearchPlaceholder.Visibility = Visibility.Visible;

            // 切換到（或留在）瀏覽模式，顯示搜尋結果
            if (!_isBrowseMode)
                await SwitchToBrowseModeAsync(keyword);
            else
                await LoadBrowsePanelAsync(keyword);

            // 捲動到選取的條目
            ScrollToEntryDate(result.Date);
        }
    }

    private void ScrollToEntryDate(DateTime date)
    {
        foreach (UIElement child in BrowseEntriesPanel.Children)
        {
            if (child is System.Windows.Controls.Border b &&
                b.Tag is DateTime d && d.Date == date.Date)
            {
                b.BringIntoView();
                break;
            }
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
    // Phase 3-C：混合語意搜尋
    // ════════════════════════════════════════

    /// <summary>
    /// 混合搜尋：向量語意 × 0.7 + 關鍵字命中 × 0.3。
    /// EmbeddingService 未就緒時自動降級為純 LIKE 搜尋。
    /// </summary>
    /// <param name="keyword">搜尋關鍵字</param>
    /// <param name="takeLimit">回傳筆數上限，0 = 不限</param>
    internal async Task<List<DiaryEntry>> HybridSearchAsync(string keyword, int takeLimit = 0)
    {
        // ── Fallback：模型尚未就緒時使用 LIKE ──
        if (!_embeddingService.IsReady || _vectorStore.Count == 0)
        {
            var fallback = takeLimit > 0
                ? await _diaryService.SearchAsync(keyword)
                : await _diaryService.SearchForBrowseAsync(keyword);
            return takeLimit > 0 ? fallback.Take(takeLimit).ToList() : fallback;
        }

        // ── 1. 語意搜尋（Top-60 候選）──
        float[] queryEmbed;
        try { queryEmbed = _embeddingService.GetEmbedding(keyword); }
        catch
        {
            var fallback = takeLimit > 0
                ? await _diaryService.SearchAsync(keyword)
                : await _diaryService.SearchForBrowseAsync(keyword);
            return takeLimit > 0 ? fallback.Take(takeLimit).ToList() : fallback;
        }

        var semanticHits = _vectorStore.SearchTopK(queryEmbed, k: 60);

        // ── 2. 關鍵字 LIKE 搜尋 ──
        var keywordEntries = await _diaryService.SearchForBrowseAsync(keyword);
        var keywordIds     = keywordEntries.Select(e => e.Id).ToHashSet();

        // ── 3. 合併 ID，從 DB 補取語意命中但關鍵字未命中的條目 ──
        var missingIds = semanticHits
            .Select(h => h.EntryId)
            .Except(keywordIds)
            .ToList();

        List<DiaryEntry> missingEntries = missingIds.Count > 0
            ? await _diaryService.GetEntriesByIdsAsync(missingIds)
            : new List<DiaryEntry>();

        var entryMap = keywordEntries
            .Concat(missingEntries)
            .ToDictionary(e => e.Id);

        // ── 4. 混合評分 ──
        var semScoreDict = semanticHits.ToDictionary(h => h.EntryId, h => h.Score);
        float maxSem     = semScoreDict.Values.Any() ? semScoreDict.Values.Max() : 1f;
        if (maxSem < 1e-6f) maxSem = 1f;

        var allIds = entryMap.Keys.ToList();

        var scored = allIds
            .Select(id =>
            {
                var entry    = entryMap[id];
                float semNorm = semScoreDict.TryGetValue(id, out var s) ? s / maxSem : 0f;
                float kwHit   = keywordIds.Contains(id) ? 1f : 0f;
                float pin     = entry.IsPinned ? 0.1f : 0f;
                float hybrid  = semNorm * 0.7f + kwHit * 0.3f + pin;
                return (entry, hybrid);
            })
            .OrderByDescending(x => x.hybrid)
            .Select(x => x.entry)
            .ToList();

        return takeLimit > 0 ? scored.Take(takeLimit).ToList() : scored;
    }
}
