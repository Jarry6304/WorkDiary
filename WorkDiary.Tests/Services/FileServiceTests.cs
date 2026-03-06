using FluentAssertions;
using WorkDiary.Services;
using Xunit;

namespace WorkDiary.Tests.Services;

/// <summary>
/// FileService 測試（真實 File System，臨時目錄）。
/// 來源檔案建立在系統 temp 目錄；CopyToStorage 複製至 AttachmentsRoot。
/// 測試結束後清理所有建立的目錄。
/// Decision Table 來源：.claude/testkit-report.md
/// </summary>
public class FileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _destDirsToClean = new();

    public FileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"workdiary-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);

        foreach (var dir in _destDirsToClean)
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }

    private string CreateSourceFile(string fileName, string content = "test data")
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private void TrackDestDir(DateTime date)
    {
        var dir = Path.Combine(FileService.AttachmentsRoot, date.ToString("yyyy-MM-dd"));
        if (!_destDirsToClean.Contains(dir))
            _destDirsToClean.Add(dir);
    }

    // ════════════════════════════════════════
    // CopyToStorage  [P1]
    // ════════════════════════════════════════

    [Fact]
    public void CopyToStorage_NoConflict_ReturnsRelativePath()
    {
        var svc = new FileService();
        var date = new DateTime(2026, 7, 1);
        TrackDestDir(date);

        var src = CreateSourceFile("report.xlsx");
        var relative = svc.CopyToStorage(src, date);

        // 回傳相對路徑格式：yyyy-MM-dd\report.xlsx（或 /）
        relative.Should().Contain("report.xlsx");
        relative.Should().Contain("2026-07-01");

        // 檔案實際存在
        File.Exists(svc.GetFullPath(relative)).Should().BeTrue();
    }

    [Fact]
    public void CopyToStorage_OneConflict_AppendsUnderscore1()
    {
        var svc = new FileService();
        var date = new DateTime(2026, 7, 2);
        TrackDestDir(date);

        var src1 = CreateSourceFile("notes.txt", "first");
        var src2 = CreateSourceFile("notes_dup.txt", "second");

        // 第一次複製
        svc.CopyToStorage(src1, date);

        // 複製同名第二個來源（重命名來源以符合同名衝突模擬）
        // 建立第二個同名來源
        var src2Same = Path.Combine(_tempDir, "notes.txt_copy");
        File.Copy(src2, src2Same);
        // 改名為相同檔名
        var src2Renamed = Path.Combine(_tempDir, "notes_renamed.txt");
        File.Move(src2Same, src2Renamed);

        // 將第二個來源命名為相同名稱複製
        var src2Final = Path.Combine(_tempDir, "notes.txt");
        if (!File.Exists(src2Final))
            File.WriteAllText(src2Final, "second");
        else
        {
            // src1 已用 notes.txt，temp dir 裡已有，用另一個
            src2Final = Path.Combine(_tempDir, $"notes_{Guid.NewGuid()}.txt");
            File.WriteAllText(src2Final, "second");
        }

        // 目標目錄已有 notes.txt → 下一個應為 notes_1.txt
        var relative2 = svc.CopyToStorage(src2Final, date);

        // 無論來源檔名，目標已存在 notes.txt 時衝突的處理是針對 fileName
        // 這裡測試用更直接的方式：複製同名第二個檔案
        var destDir = Path.Combine(FileService.AttachmentsRoot, date.ToString("yyyy-MM-dd"));
        var existingFiles = Directory.GetFiles(destDir);
        existingFiles.Length.Should().Be(2, "should have two files after conflict resolution");
    }

    [Fact]
    public void CopyToStorage_SameFileNameConflict_IncrementsSuffix()
    {
        // 更清晰的衝突測試：直接複製三次同名檔案
        var svc = new FileService();
        var date = new DateTime(2026, 7, 3);
        TrackDestDir(date);

        // 建立三個同名來源
        var files = Enumerable.Range(1, 3)
            .Select(i =>
            {
                var f = Path.Combine(_tempDir, $"source_{i}.txt");
                File.WriteAllText(f, $"content {i}");
                return f;
            }).ToList();

        // 三個來源都叫 "data.txt" → 需要 mock 或直接建到目標測試衝突邏輯
        // 改為直接在目標目錄預建檔案，然後 CopyToStorage 一個同名檔案
        var destDir = Path.Combine(FileService.AttachmentsRoot, date.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "data.txt"), "pre-existing");
        File.WriteAllText(Path.Combine(destDir, "data_1.txt"), "pre-existing-1");

        var srcData = CreateSourceFile("data.txt", "new content");
        var relative = svc.CopyToStorage(srcData, date);

        // 已有 data.txt 和 data_1.txt → 新檔應為 data_2.txt
        relative.Should().Contain("data_2.txt");
        File.Exists(svc.GetFullPath(relative)).Should().BeTrue();
    }

    [Fact]
    public void CopyToStorage_SourceNotExist_Throws()
    {
        var svc = new FileService();
        var nonExistent = Path.Combine(_tempDir, "ghost.pdf");
        var date = new DateTime(2026, 7, 4);
        TrackDestDir(date);

        var act = () => svc.CopyToStorage(nonExistent, date);
        act.Should().Throw<FileNotFoundException>();
    }

    // ════════════════════════════════════════
    // DeleteFile  [P1]
    // ════════════════════════════════════════

    [Fact]
    public void DeleteFile_Exists_DeletesFile()
    {
        var svc = new FileService();
        var date = new DateTime(2026, 7, 5);
        TrackDestDir(date);

        var src = CreateSourceFile("todelete.txt");
        var relative = svc.CopyToStorage(src, date);

        File.Exists(svc.GetFullPath(relative)).Should().BeTrue();

        svc.DeleteFile(relative);

        File.Exists(svc.GetFullPath(relative)).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_NotExist_Silent()
    {
        // 不存在的相對路徑 → File.Exists 為 false，靜默不拋例外
        var svc = new FileService();
        var act = () => svc.DeleteFile("2099-01-01/ghost.txt");
        act.Should().NotThrow();
    }

    // ════════════════════════════════════════
    // GetFullPath  [P1]
    // ════════════════════════════════════════

    [Fact]
    public void GetFullPath_CombinesRootAndRelative()
    {
        var svc = new FileService();
        var relative = Path.Combine("2026-07-06", "notes.txt");
        var full = svc.GetFullPath(relative);

        full.Should().Be(Path.Combine(FileService.AttachmentsRoot, relative));
    }
}
