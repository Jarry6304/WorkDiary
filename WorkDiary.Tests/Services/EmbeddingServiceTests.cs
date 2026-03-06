using FluentAssertions;
using System.Reflection;
using WorkDiary.Services;
using Xunit;

namespace WorkDiary.Tests.Services;

/// <summary>
/// EmbeddingService 測試。
/// P1：純函數（CosineSimilarity）、未初始化防衛、Tokenizer（反射）。
/// P2：GetEmbedding 需真實 ONNX 模型，標記 Integration，CI 可排除。
/// Decision Table 來源：.claude/testkit-report.md
/// </summary>
public class EmbeddingServiceTests
{
    // ════════════════════════════════════════
    // CosineSimilarity（靜態純函數）  [P1]
    // ════════════════════════════════════════

    [Fact]
    public void CosineSimilarity_SameVector_ReturnsOne()
    {
        // L2 正規化相同向量 → dot product = 1.0
        var vec = Normalize(new float[] { 3f, 4f });
        var result = EmbeddingService.CosineSimilarity(vec, vec);
        result.Should().BeApproximately(1.0f, precision: 1e-5f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };
        var result = EmbeddingService.CosineSimilarity(a, b);
        result.Should().BeApproximately(0f, precision: 1e-5f);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegOne()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { -1f, 0f };
        var result = EmbeddingService.CosineSimilarity(a, b);
        result.Should().BeApproximately(-1.0f, precision: 1e-5f);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        // 零向量 dot product = 0
        var zero = new float[] { 0f, 0f };
        var other = new float[] { 1f, 0f };
        var result = EmbeddingService.CosineSimilarity(zero, other);
        result.Should().BeApproximately(0f, precision: 1e-5f);
    }

    // ════════════════════════════════════════
    // GetEmbedding（未初始化防衛）  [P1]
    // ════════════════════════════════════════

    [Fact]
    public void GetEmbedding_NotInitialized_ThrowsInvalidOperationException()
    {
        // IsReady = false → 拋出 InvalidOperationException
        var svc = new EmbeddingService();
        var act = () => svc.GetEmbedding("測試文字");
        act.Should().Throw<InvalidOperationException>();
    }

    // ════════════════════════════════════════
    // Tokenize（透過反射存取私有方法）  [P1]
    // ════════════════════════════════════════

    /// <summary>
    /// 透過反射設置最小詞彙表並呼叫私有 Tokenize 方法，
    /// 避免需要下載實際 vocab 檔（93MB 模型）。
    /// </summary>
    private static (int[] ids, int[] mask, int[] typeIds) InvokeTokenize(EmbeddingService svc, string text)
    {
        var method = typeof(EmbeddingService).GetMethod(
            "Tokenize", BindingFlags.NonPublic | BindingFlags.Instance)!;
        dynamic result = method.Invoke(svc, new object[] { text })!;
        return (result.Item1, result.Item2, result.Item3);
    }

    private static EmbeddingService CreateWithMinimalVocab()
    {
        var svc = new EmbeddingService();
        var vocabField = typeof(EmbeddingService)
            .GetField("_vocab", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var vocab = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "[PAD]",  0  },
            { "[UNK]",  100 },
            { "[CLS]",  101 },
            { "[SEP]",  102 },
            { "hello",  1000 },
            { "world",  1001 },
            { "##ld",   1002 },
        };
        vocabField.SetValue(svc, vocab);
        return svc;
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsClsSepOnly()
    {
        // 空字串 → ids[0]=CLS(101), ids[1]=SEP(102), 其餘 0
        //           mask[0]=1, mask[1]=1, 其餘 0
        var svc = CreateWithMinimalVocab();
        var (ids, mask, _) = InvokeTokenize(svc, "");

        ids[0].Should().Be(101, "first token should be [CLS]");
        ids[1].Should().Be(102, "second token should be [SEP]");
        ids[2].Should().Be(0,   "remaining tokens should be padding");

        mask[0].Should().Be(1);
        mask[1].Should().Be(1);
        mask[2].Should().Be(0);
    }

    [Fact]
    public void Tokenize_LongWord_ReturnsUnk()
    {
        // 超過 100 字元的單詞 → WordPieceTokenize 回傳 [UNK]=100
        var svc = CreateWithMinimalVocab();
        var longWord = new string('a', 101);
        var (ids, mask, _) = InvokeTokenize(svc, longWord);

        // ids[0]=CLS, ids[1]=UNK(100), ids[2]=SEP
        ids[0].Should().Be(101);
        ids[1].Should().Be(100, "long word should map to [UNK]");
        ids[2].Should().Be(102);
        mask[1].Should().Be(1);
    }

    // ════════════════════════════════════════
    // GetEmbedding（需真實 ONNX 模型）  [P2]
    // ════════════════════════════════════════

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEmbedding_ReturnsNormalizedVector()
    {
        // 需要 InitializeAsync 下載 93MB 模型，CI 以 --filter Category!=Integration 排除
        var svc = new EmbeddingService();
        await svc.InitializeAsync();

        var vec = svc.GetEmbedding("今天天氣很好");

        vec.Should().HaveCount(EmbeddingService.EmbeddingDim);

        // L2 Norm ≈ 1.0（向量已正規化）
        var norm = Math.Sqrt(vec.Sum(v => v * v));
        norm.Should().BeApproximately(1.0, precision: 1e-4);

        svc.Dispose();
    }

    // ── 工具方法 ──

    private static float[] Normalize(float[] v)
    {
        var norm = (float)Math.Sqrt(v.Sum(x => x * x));
        return v.Select(x => x / norm).ToArray();
    }
}
