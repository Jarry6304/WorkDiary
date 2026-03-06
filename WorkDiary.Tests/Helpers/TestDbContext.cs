using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkDiary.Data;

namespace WorkDiary.Tests.Helpers;

/// <summary>
/// AppDbContext 的測試替身：使用 SQLite In-Memory 連線取代實體檔案 DB。
/// 每個測試建立獨立連線，避免跨測試狀態污染。
/// </summary>
internal sealed class TestDbContext : AppDbContext
{
    private readonly SqliteConnection _connection;

    public TestDbContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite(_connection);

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}

internal static class DbFactory
{
    /// <summary>
    /// 建立並初始化 In-Memory SQLite 測試用 AppDbContext。
    /// EnsureCreated() 依據 EF Core 模型建立所有資料表（含 DiaryEntryTag junction table）。
    /// </summary>
    public static AppDbContext Create()
    {
        var db = new TestDbContext();
        db.Database.EnsureCreated();
        return db;
    }
}
