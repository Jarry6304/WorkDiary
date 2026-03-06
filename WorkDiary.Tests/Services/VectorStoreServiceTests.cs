using FluentAssertions;
using WorkDiary.Services;
using Xunit;

namespace WorkDiary.Tests.Services;

/// <summary>
/// VectorStoreService 單元測試（純記憶體）。
/// Decision Table 來源：.claude/testkit-report.md
/// </summary>
public class VectorStoreServiceTests
{
    // ════════════════════════════════════════
    // FloatToBlob / FloatFromBlob  [P0]
    // ════════════════════════════════════════

    [Fact]
    public void FloatToBlob_RoundTrip_BitExact()
    {
        // 往返序列化：float[] → byte[] → float[] 必須 bit-exact
        var original = new float[] { 1.5f, -2.25f, 0f, float.MaxValue, float.MinValue };
        var blob = VectorStoreService.FloatToBlob(original);
        var restored = VectorStoreService.FloatFromBlob(blob);

        restored.Should().Equal(original);
    }

    [Fact]
    public void FloatToBlob_ReturnsCorrectByteLength()
    {
        // N 個 float → N×4 bytes
        var input = new float[16];
        var blob = VectorStoreService.FloatToBlob(input);
        blob.Should().HaveCount(16 * sizeof(float));
    }

    [Fact]
    public void FloatToBlob_EmptyArray_ReturnsEmptyBytes()
    {
        var blob = VectorStoreService.FloatToBlob(Array.Empty<float>());
        blob.Should().BeEmpty();
    }

    [Fact]
    public void FloatFromBlob_EmptyBytes_ReturnsEmptyArray()
    {
        var floats = VectorStoreService.FloatFromBlob(Array.Empty<byte>());
        floats.Should().BeEmpty();
    }

    // ════════════════════════════════════════
    // SearchTopK  [P0]
    // ════════════════════════════════════════

    [Fact]
    public void SearchTopK_MoreThanK_ReturnsK()
    {
        // N > k → 回傳剛好 k 筆
        var store = new VectorStoreService();
        for (int i = 0; i < 10; i++)
        {
            var vec = new float[4];
            vec[i % 4] = 1f;   // 簡單線性獨立向量
            store.Upsert(i, vec);
        }

        var query = new float[] { 1f, 0f, 0f, 0f };
        var results = store.SearchTopK(query, k: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public void SearchTopK_LessThanK_ReturnsAll()
    {
        // N < k → 回傳全部 N 筆，不拋例外
        var store = new VectorStoreService();
        store.Upsert(1, new float[] { 1f, 0f });
        store.Upsert(2, new float[] { 0f, 1f });

        var results = store.SearchTopK(new float[] { 1f, 0f }, k: 10);

        results.Should().HaveCount(2);
    }

    [Fact]
    public void SearchTopK_EmptyStore_ReturnsEmpty()
    {
        // 空庫 → 回傳空清單
        var store = new VectorStoreService();
        var results = store.SearchTopK(new float[] { 1f, 0f }, k: 5);

        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchTopK_ResultsOrderedByScoreDescending()
    {
        // 結果依 CosineSimilarity 降冪排列
        var store = new VectorStoreService();
        // query = [1, 0]
        // id=1: [1, 0] → similarity 1.0
        // id=2: [0, 1] → similarity 0.0
        // id=3: [-1, 0] → similarity -1.0
        store.Upsert(1, new float[] { 1f, 0f });
        store.Upsert(2, new float[] { 0f, 1f });
        store.Upsert(3, new float[] { -1f, 0f });

        var results = store.SearchTopK(new float[] { 1f, 0f }, k: 3);

        results[0].EntryId.Should().Be(1);
        results[0].Score.Should().BeApproximately(1.0f, precision: 1e-5f);
        results[2].EntryId.Should().Be(3);
        results[2].Score.Should().BeApproximately(-1.0f, precision: 1e-5f);
    }

    // ════════════════════════════════════════
    // Upsert / Remove  [P1]
    // ════════════════════════════════════════

    [Fact]
    public void Upsert_NewEntry_IncreasesCount()
    {
        var store = new VectorStoreService();
        store.Upsert(42, new float[] { 1f, 0f });
        store.Count.Should().Be(1);
    }

    [Fact]
    public void Upsert_ExistingEntry_OverwritesVector()
    {
        var store = new VectorStoreService();
        store.Upsert(1, new float[] { 1f, 0f });
        store.Upsert(1, new float[] { 0f, 1f });

        store.Count.Should().Be(1);
        // 驗證新向量已覆蓋：搜尋 [0,1] 應回傳 id=1 且分數 ≈ 1.0
        var results = store.SearchTopK(new float[] { 0f, 1f }, k: 1);
        results[0].EntryId.Should().Be(1);
        results[0].Score.Should().BeApproximately(1.0f, precision: 1e-5f);
    }

    [Fact]
    public void Remove_ExistingEntry_DecreasesCount()
    {
        var store = new VectorStoreService();
        store.Upsert(10, new float[] { 1f });
        store.Remove(10);
        store.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_NonExistingEntry_Silent()
    {
        // Dictionary.Remove 對不存在的 key 靜默，不拋例外
        var store = new VectorStoreService();
        var act = () => store.Remove(999);
        act.Should().NotThrow();
        store.Count.Should().Be(0);
    }
}
