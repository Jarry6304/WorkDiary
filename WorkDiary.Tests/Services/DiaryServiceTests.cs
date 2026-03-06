using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkDiary.Models;
using WorkDiary.Services;
using WorkDiary.Tests.Helpers;
using Xunit;

namespace WorkDiary.Tests.Services;

/// <summary>
/// DiaryService 單元測試（In-Memory SQLite）。
/// Decision Table 來源：.claude/testkit-report.md
/// </summary>
public class DiaryServiceTests
{
    // ════════════════════════════════════════
    // SaveContentAsync  [P0]
    // ════════════════════════════════════════

    [Fact]
    public async Task SaveContentAsync_EntryExists_UpdatesContent()
    {
        // 日誌存在 + 任意 content → ExecuteUpdate 更新 Content
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 1, 1);
        db.Set<DiaryEntry>().Add(new DiaryEntry
        {
            Date = date, Content = "original",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        await svc.SaveContentAsync(date, "updated");

        var result = await db.Set<DiaryEntry>().AsNoTracking().FirstAsync(e => e.Date == date);
        result.Content.Should().Be("updated");
    }

    [Fact]
    public async Task SaveContentAsync_EntryNotExist_NonEmpty_InsertsNewRecord()
    {
        // 日誌不存在 + content 非空 → INSERT 新記錄
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 1, 2);

        var svc = new DiaryService(db);
        await svc.SaveContentAsync(date, "new content");

        var count = await db.Set<DiaryEntry>().CountAsync(e => e.Date == date);
        count.Should().Be(1);

        var entry = await db.Set<DiaryEntry>().AsNoTracking().FirstAsync(e => e.Date == date);
        entry.Content.Should().Be("new content");
    }

    [Fact]
    public async Task SaveContentAsync_EntryNotExist_EmptyContent_SkipsInsert()
    {
        // 日誌不存在 + content 空白 → 不寫入
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 1, 3);

        var svc = new DiaryService(db);
        await svc.SaveContentAsync(date, "   ");

        var count = await db.Set<DiaryEntry>().CountAsync(e => e.Date == date);
        count.Should().Be(0);
    }

    // ════════════════════════════════════════
    // GetOrCreateEntryAsync  [P1]
    // ════════════════════════════════════════

    [Fact]
    public async Task GetOrCreateEntryAsync_Exists_ReturnsExisting()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 2, 1);
        db.Set<DiaryEntry>().Add(new DiaryEntry
        {
            Date = date, Content = "existing",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var result = await svc.GetOrCreateEntryAsync(date);

        result.Content.Should().Be("existing");
        db.Set<DiaryEntry>().Count().Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateEntryAsync_NotExists_CreatesAndReturns()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 2, 2);

        var svc = new DiaryService(db);
        var result = await svc.GetOrCreateEntryAsync(date);

        result.Date.Should().Be(date.Date);
        result.Content.Should().Be(string.Empty);
        db.Set<DiaryEntry>().Count().Should().Be(1);
    }

    // ════════════════════════════════════════
    // SearchAsync  [P0]
    // ════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_MatchContent_ReturnsResults()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 3, 1);
        db.Set<DiaryEntry>().Add(new DiaryEntry
        {
            Date = date, Content = "今天開會討論季報",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var results = await svc.SearchAsync("季報");

        results.Should().HaveCount(1);
        results[0].Content.Should().Contain("季報");
    }

    [Fact]
    public async Task SearchAsync_MatchTags_ReturnsResults()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 3, 2);
        db.Set<DiaryEntry>().Add(new DiaryEntry
        {
            Date = date, Content = "普通紀錄", Tags = "工作,重要",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var results = await svc.SearchAsync("重要");

        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 3, 3);
        db.Set<DiaryEntry>().Add(new DiaryEntry
        {
            Date = date, Content = "今天晴天",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var results = await svc.SearchAsync("xyz不存在關鍵字");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_OverTenMatches_ReturnsMaxTen()
    {
        await using var db = DbFactory.Create();
        for (int i = 1; i <= 12; i++)
        {
            db.Set<DiaryEntry>().Add(new DiaryEntry
            {
                Date = new DateTime(2026, 3, i), Content = $"今天工作記錄 {i}",
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
            });
        }
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var results = await svc.SearchAsync("工作");

        results.Should().HaveCount(10);
    }

    [Fact]
    public async Task SearchAsync_PinnedEntry_AppearsFirst()
    {
        await using var db = DbFactory.Create();
        db.Set<DiaryEntry>().AddRange(
            new DiaryEntry { Date = new DateTime(2026, 3, 5), Content = "工作記錄 A", IsPinned = false, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now },
            new DiaryEntry { Date = new DateTime(2026, 3, 6), Content = "工作記錄 B 置頂", IsPinned = true, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now }
        );
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var results = await svc.SearchAsync("工作");

        results.Should().HaveCount(2);
        results[0].IsPinned.Should().BeTrue();
    }

    // ════════════════════════════════════════
    // SearchForBrowseAsync  [P0]
    // ════════════════════════════════════════

    [Fact]
    public async Task SearchForBrowseAsync_MatchContent_NoPaging()
    {
        await using var db = DbFactory.Create();
        for (int i = 1; i <= 15; i++)
        {
            db.Set<DiaryEntry>().Add(new DiaryEntry
            {
                Date = new DateTime(2026, 4, i), Content = $"瀏覽記錄 {i}",
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
            });
        }
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var results = await svc.SearchForBrowseAsync("瀏覽");

        // 無 10 筆上限
        results.Should().HaveCount(15);
    }

    [Fact]
    public async Task SearchForBrowseAsync_NoMatch_ReturnsEmpty()
    {
        await using var db = DbFactory.Create();
        db.Set<DiaryEntry>().Add(new DiaryEntry
        {
            Date = new DateTime(2026, 4, 16), Content = "普通內容",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        var results = await svc.SearchForBrowseAsync("不存在");

        results.Should().BeEmpty();
    }

    // ════════════════════════════════════════
    // SyncTagsAsync  [P1]
    // ════════════════════════════════════════

    [Fact]
    public async Task SyncTagsAsync_EntryNotExist_DoesNothing()
    {
        await using var db = DbFactory.Create();
        var svc = new DiaryService(db);

        // 日誌不存在 → 直接 return，不拋例外
        await svc.Invoking(s => s.SyncTagsAsync(new DateTime(2099, 1, 1), new List<string> { "A" }))
            .Should().NotThrowAsync();

        var tagCount = await db.Set<WorkDiary.Models.Tag>().CountAsync();
        tagCount.Should().Be(0);
    }

    [Fact]
    public async Task SyncTagsAsync_EmptyTags_ClearsJunction()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 5, 1);
        db.Set<DiaryEntry>().Add(new DiaryEntry { Date = date, Content = "test", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        // 先建一個 tag
        await svc.SyncTagsAsync(date, new List<string> { "OldTag" });
        // 再同步為空
        await svc.SyncTagsAsync(date, new List<string>());

        var loaded = await db.Set<DiaryEntry>()
            .AsNoTracking().Include(e => e.TagEntities)
            .FirstAsync(e => e.Date == date);
        loaded.TagEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncTagsAsync_NewTags_CreatesTagsAndJunction()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 5, 2);
        db.Set<DiaryEntry>().Add(new DiaryEntry { Date = date, Content = "test", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        await svc.SyncTagsAsync(date, new List<string> { "Alpha", "Beta" });

        var loaded = await db.Set<DiaryEntry>()
            .AsNoTracking().Include(e => e.TagEntities)
            .FirstAsync(e => e.Date == date);
        loaded.TagEntities.Should().HaveCount(2);
        loaded.TagEntities.Select(t => t.Name).Should().Contain("Alpha").And.Contain("Beta");
    }

    [Fact]
    public async Task SyncTagsAsync_DuplicateCasing_DeduplicatesTags()
    {
        // ["Alpha", "alpha"] → Distinct(OrdinalIgnoreCase) → 只建 1 筆
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 5, 3);
        db.Set<DiaryEntry>().Add(new DiaryEntry { Date = date, Content = "test", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        await svc.SyncTagsAsync(date, new List<string> { "Alpha", "alpha" });

        var loaded = await db.Set<DiaryEntry>()
            .AsNoTracking().Include(e => e.TagEntities)
            .FirstAsync(e => e.Date == date);
        loaded.TagEntities.Should().HaveCount(1);
    }

    [Fact]
    public async Task SyncTagsAsync_TagAlreadyExists_InsertOrIgnoreNoDuplicate()
    {
        // Tag 已存在 → INSERT OR IGNORE 不重複建
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 5, 4);
        db.Set<DiaryEntry>().Add(new DiaryEntry { Date = date, Content = "test", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        // 第一次建
        await svc.SyncTagsAsync(date, new List<string> { "ExistingTag" });
        // 第二次同步相同 tag
        await svc.SyncTagsAsync(date, new List<string> { "ExistingTag" });

        var tagCount = await db.Set<WorkDiary.Models.Tag>().CountAsync();
        tagCount.Should().Be(1);
    }

    // ════════════════════════════════════════
    // SetPinnedAsync / SaveTagsAsync  [P1]
    // ════════════════════════════════════════

    [Fact]
    public async Task SetPinnedAsync_EntryExists_UpdatesPin()
    {
        await using var db = DbFactory.Create();
        var date = new DateTime(2026, 6, 1);
        db.Set<DiaryEntry>().Add(new DiaryEntry
        {
            Date = date, Content = "test", IsPinned = false,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var svc = new DiaryService(db);
        await svc.SetPinnedAsync(date, true);

        var entry = await db.Set<DiaryEntry>().AsNoTracking().FirstAsync(e => e.Date == date);
        entry.IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task SetPinnedAsync_EntryNotExist_NoSideEffect()
    {
        await using var db = DbFactory.Create();
        var svc = new DiaryService(db);

        // 不存在的日誌 → ExecuteUpdate 回傳 0，不拋例外
        await svc.Invoking(s => s.SetPinnedAsync(new DateTime(2099, 1, 1), true))
            .Should().NotThrowAsync();

        var count = await db.Set<DiaryEntry>().CountAsync();
        count.Should().Be(0);
    }
}
