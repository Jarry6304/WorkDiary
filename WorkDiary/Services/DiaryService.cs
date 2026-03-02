using Microsoft.EntityFrameworkCore;
using WorkDiary.Data;
using WorkDiary.Models;

namespace WorkDiary.Services;

public class DiaryService
{
    private readonly AppDbContext _db;

    public DiaryService(AppDbContext db) => _db = db;

    // ── 讀取（AsNoTracking，始終取得 DB 最新資料）──

    public async Task<DiaryEntry?> GetEntryAsync(DateTime date)
    {
        return await _db.DiaryEntries
            .Include(e => e.Attachments)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Date == date.Date);
    }

    // ── 寫入 ──

    /// <summary>
    /// 取得或建立指定日期的 DiaryEntry（追蹤狀態，供後續加入附件使用）。
    /// </summary>
    public async Task<DiaryEntry> GetOrCreateEntryAsync(DateTime date)
    {
        var existing = await _db.DiaryEntries
            .Include(e => e.Attachments)
            .FirstOrDefaultAsync(e => e.Date == date.Date);

        if (existing is not null)
            return existing;

        var entry = new DiaryEntry
        {
            Date      = date.Date,
            Content   = string.Empty,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.DiaryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>
    /// 自動儲存文字內容：
    /// - 有記錄 → 直接 UPDATE（跳過 EF 追蹤）
    /// - 無記錄且內容非空 → INSERT
    /// - 無記錄且內容空白 → 略過（不寫入空記錄）
    /// </summary>
    public async Task SaveContentAsync(DateTime date, string content)
    {
        var rows = await _db.DiaryEntries
            .Where(e => e.Date == date.Date)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Content,   content)
                .SetProperty(e => e.UpdatedAt, DateTime.Now));

        if (rows == 0 && !string.IsNullOrWhiteSpace(content))
        {
            _db.DiaryEntries.Add(new DiaryEntry
            {
                Date      = date.Date,
                Content   = content,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
    }

    public async Task AddAttachmentAsync(FileAttachment attachment)
    {
        _db.FileAttachments.Add(attachment);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAttachmentAsync(int attachmentId)
    {
        await _db.FileAttachments
            .Where(f => f.Id == attachmentId)
            .ExecuteDeleteAsync();
    }
}
