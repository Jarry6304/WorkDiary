namespace WorkDiary.Models;

public class DiaryEntry
{
    public int Id { get; set; }

    /// <summary>日誌日期（時間固定為 00:00:00）</summary>
    public DateTime Date { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>是否置頂</summary>
    public bool IsPinned { get; set; }

    /// <summary>副標題標籤（逗號分隔儲存）</summary>
    public string Tags { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<FileAttachment> Attachments { get; set; } = new();

    /// <summary>Tag 關係表（多對多）</summary>
    public ICollection<Tag> TagEntities { get; set; } = new List<Tag>();

    /// <summary>all-MiniLM-L6-v2 語意向量（float[384] 序列化為 BLOB）</summary>
    public byte[]? Embedding { get; set; }
}
