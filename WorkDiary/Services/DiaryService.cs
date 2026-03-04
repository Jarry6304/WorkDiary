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

    /// <summary>瀏覽模式：取所有日誌（含附件），置頂優先，日期降冪。</summary>
    public async Task<List<DiaryEntry>> GetAllEntriesAsync()
    {
        return await _db.DiaryEntries
            .Include(e => e.Attachments)
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.Date)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>瀏覽模式全文搜尋：搜尋內文 + 標籤，含附件，置頂優先。</summary>
    public async Task<List<DiaryEntry>> SearchForBrowseAsync(string keyword)
    {
        return await _db.DiaryEntries
            .Include(e => e.Attachments)
            .Where(e => EF.Functions.Like(e.Content, $"%{keyword}%")
                     || EF.Functions.Like(e.Tags, $"%{keyword}%"))
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.Date)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>儲存標籤字串。</summary>
    public async Task SaveTagsAsync(DateTime date, string tags)
    {
        await _db.DiaryEntries
            .Where(e => e.Date == date.Date)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Tags, tags));
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

    /// <summary>快速搜尋（Popup 用），搜尋內文 + 標籤，置頂者優先，最多 10 筆。</summary>
    public async Task<List<DiaryEntry>> SearchAsync(string keyword)
    {
        return await _db.DiaryEntries
            .Where(e => EF.Functions.Like(e.Content, $"%{keyword}%")
                     || EF.Functions.Like(e.Tags, $"%{keyword}%"))
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.Date)
            .Take(10)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Tag 關係表 ──

    /// <summary>取所有已知標籤名稱（用於輸入自動完成）。</summary>
    public async Task<List<string>> GetAllTagNamesAsync()
    {
        return await _db.Tags
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync();
    }

    /// <summary>
    /// 將 CSV 標籤字串同步至 Tag 關係表（使用原生 SQL 避免 EF 追蹤複雜度）。
    /// </summary>
    public async Task SyncTagsAsync(DateTime date, List<string> tagNames)
    {
        var entry = await _db.DiaryEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Date == date.Date);

        if (entry == null) return;

        // 清除舊關聯
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM DiaryEntryTag WHERE DiaryEntryId = {0}", entry.Id);

        foreach (var name in tagNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // 確保 Tag 記錄存在
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO Tags (Name) VALUES ({0})", name);

            // 建立 junction 記錄
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO DiaryEntryTag (DiaryEntryId, TagId) " +
                "SELECT {0}, Id FROM Tags WHERE Name = {1}",
                entry.Id, name);
        }
    }

    /// <summary>首次啟動時，將現有 CSV Tags 欄位遷移到 Tags 表（只建標籤名稱，無 junction）。</summary>
    public async Task MigrateTagsCsvToTableAsync()
    {
        var allTagStrings = await _db.DiaryEntries
            .Where(e => e.Tags != string.Empty)
            .Select(e => e.Tags)
            .AsNoTracking()
            .ToListAsync();

        var allNames = allTagStrings
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO Tags (Name) VALUES ({0})", name);
    }

    // ── Embedding ──

    /// <summary>更新指定日誌的語意向量。</summary>
    public async Task UpdateEmbeddingAsync(int entryId, byte[] embedding)
    {
        await _db.DiaryEntries
            .Where(e => e.Id == entryId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Embedding, embedding));
    }

    /// <summary>取得尚未建立語意向量的日誌清單（Id + 內容）。</summary>
    public async Task<List<(int Id, string Content)>> GetEntriesWithoutEmbeddingAsync()
    {
        var results = await _db.DiaryEntries
            .Where(e => e.Embedding == null && e.Content != string.Empty)
            .Select(e => new { e.Id, e.Content })
            .AsNoTracking()
            .ToListAsync();

        return results.Select(x => (x.Id, x.Content)).ToList();
    }
}
