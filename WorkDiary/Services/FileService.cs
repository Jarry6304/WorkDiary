using System.IO;

namespace WorkDiary.Services;

public class FileService
{
    /// <summary>附件根目錄：%LOCALAPPDATA%\WorkDiary\attachments\</summary>
    public static readonly string AttachmentsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkDiary", "attachments");

    public FileService()
    {
        Directory.CreateDirectory(AttachmentsRoot);
    }

    /// <summary>
    /// 複製來源檔案到 attachments/{date}/ 資料夾，自動處理同名衝突。
    /// 回傳相對路徑，例如 2026-03-02\report.xlsx。
    /// </summary>
    public string CopyToStorage(string sourcePath, DateTime date)
    {
        var dateFolder = date.ToString("yyyy-MM-dd");
        var destDir   = Path.Combine(AttachmentsRoot, dateFolder);
        Directory.CreateDirectory(destDir);

        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(destDir, fileName);

        // 同名衝突時加上流水號
        if (File.Exists(destPath))
        {
            var stem  = Path.GetFileNameWithoutExtension(fileName);
            var ext   = Path.GetExtension(fileName);
            var count = 1;
            do
            {
                fileName = $"{stem}_{count++}{ext}";
                destPath = Path.Combine(destDir, fileName);
            }
            while (File.Exists(destPath));
        }

        File.Copy(sourcePath, destPath);
        return Path.Combine(dateFolder, fileName);  // 回傳相對路徑
    }

    /// <summary>由相對路徑組合出完整路徑</summary>
    public string GetFullPath(string relativePath)
        => Path.Combine(AttachmentsRoot, relativePath);

    /// <summary>刪除附件實體檔案</summary>
    public void DeleteFile(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
