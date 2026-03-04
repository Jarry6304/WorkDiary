using Microsoft.EntityFrameworkCore;
using WorkDiary.Data;

namespace WorkDiary.Services;

/// <summary>
/// 記憶體向量庫：啟動時從 SQLite BLOB 載入所有已建 embedding，
/// 提供 Upsert / Remove / TopK 搜尋 / 非同步持久化。
/// </summary>
public class VectorStoreService
{
    private readonly Dictionary<int, float[]> _store = new();

    // ── 載入 ──

    public async Task LoadAllAsync(AppDbContext db)
    {
        var entries = await db.DiaryEntries
            .Where(e => e.Embedding != null)
            .Select(e => new { e.Id, e.Embedding })
            .AsNoTracking()
            .ToListAsync();

        _store.Clear();
        foreach (var e in entries)
            _store[e.Id] = FloatFromBlob(e.Embedding!);
    }

    // ── CRUD ──

    public void Upsert(int entryId, float[] embedding) =>
        _store[entryId] = embedding;

    public void Remove(int entryId) =>
        _store.Remove(entryId);

    public async Task PersistAsync(int entryId, float[] embedding, AppDbContext db)
    {
        var blob = FloatToBlob(embedding);
        await db.DiaryEntries
            .Where(e => e.Id == entryId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Embedding, blob));
    }

    // ── 搜尋 ──

    public List<(int EntryId, float Score)> SearchTopK(float[] query, int k = 20)
    {
        return _store
            .Select(kvp => (kvp.Key, EmbeddingService.CosineSimilarity(query, kvp.Value)))
            .OrderByDescending(x => x.Item2)
            .Take(k)
            .ToList();
    }

    // ── BLOB 序列化 ──

    public static byte[] FloatToBlob(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FloatFromBlob(byte[] blob)
    {
        var floats = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, floats, 0, blob.Length);
        return floats;
    }
}
