using Microsoft.EntityFrameworkCore;
using System.IO;
using WorkDiary.Models;

namespace WorkDiary.Data;

public class AppDbContext : DbContext
{
    public DbSet<DiaryEntry>    DiaryEntries    => Set<DiaryEntry>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
    public DbSet<Tag>            Tags            => Set<Tag>();

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

        // Tag 名稱唯一索引
        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        // DiaryEntry ↔ Tag 多對多（明確指定 junction table 欄位名稱）
        modelBuilder.Entity<DiaryEntry>()
            .HasMany(e => e.TagEntities)
            .WithMany(t => t.DiaryEntries)
            .UsingEntity<Dictionary<string, object>>(
                "DiaryEntryTag",
                j => j.HasOne<Tag>().WithMany().HasForeignKey("TagId"),
                j => j.HasOne<DiaryEntry>().WithMany().HasForeignKey("DiaryEntryId"));
    }
}
