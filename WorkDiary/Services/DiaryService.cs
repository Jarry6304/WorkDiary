using Microsoft.EntityFrameworkCore;
using WorkDiary.Data;
using WorkDiary.Models;

namespace WorkDiary.Services;

public class DiaryService
{
    private readonly AppDbContext _db;

    public DiaryService(AppDbContext db) => _db = db;

    // ── 讀取 ──

    public async Task<DiaryEntry?> GetEntryAsync(DateTime date)
    {
        return await _db.DiaryEntries
            .Include(e => e.Attachments)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Date == date.Date);
    }

    // ── 寫入 ──

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
    /// - 有記錄 → UPDATE（跳過 EF 追蹤）
    /// - 無記錄且內容非空 → INSERT
    /// - 無記錄且內容空白 → 略過
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

    /// <summary>切換置頂狀態。</summary>
    public async Task SetPinnedAsync(DateTime date, bool pinned)
    {
        await _db.DiaryEntries
            .Where(e => e.Date == date.Date)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsPinned, pinned));
    }

    // ── 附件 ──

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

    // ── 搜尋 ──

    /// <summary>全文搜尋，置頂者優先，最多回傳 10 筆。</summary>
    public async Task<List<DiaryEntry>> SearchAsync(string keyword)
    {
        return await _db.DiaryEntries
            .Where(e => EF.Functions.Like(e.Content, $"%{keyword}%"))
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.Date)
            .Take(10)
            .AsNoTracking()
            .ToListAsync();
    }
}
