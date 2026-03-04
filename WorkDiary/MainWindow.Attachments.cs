using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkDiary.Models;

namespace WorkDiary;

public partial class MainWindow
{
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
}
