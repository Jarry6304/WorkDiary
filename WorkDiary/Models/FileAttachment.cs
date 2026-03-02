namespace WorkDiary.Models;

public class FileAttachment
{
    public int Id { get; set; }

    public int DiaryEntryId { get; set; }
    public DiaryEntry DiaryEntry { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    /// <summary>相對於 AttachmentsRoot 的路徑，例如 2026-03-02/report.xlsx</summary>
    public string RelativePath { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime AddedAt { get; set; }
}
