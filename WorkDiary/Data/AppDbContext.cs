using Microsoft.EntityFrameworkCore;
using WorkDiary.Models;

namespace WorkDiary.Data;

public class AppDbContext : DbContext
{
    public DbSet<DiaryEntry>    DiaryEntries    => Set<DiaryEntry>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    /// <summary>資料庫所在路徑：%LOCALAPPDATA%\WorkDiary\workdiary.db</summary>
    public static string DbPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkDiary", "workdiary.db");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // 確保目錄存在
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        optionsBuilder.UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 每天只能有一筆 DiaryEntry
        modelBuilder.Entity<DiaryEntry>()
            .HasIndex(e => e.Date)
            .IsUnique();

        // FileAttachment 隨 DiaryEntry 級聯刪除
        modelBuilder.Entity<FileAttachment>()
            .HasOne(f => f.DiaryEntry)
            .WithMany(e => e.Attachments)
            .HasForeignKey(f => f.DiaryEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
