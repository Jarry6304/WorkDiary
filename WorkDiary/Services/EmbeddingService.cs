using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;

namespace WorkDiary.Services;

/// <summary>
/// ONNX BAAI/bge-small-zh-v1.5 語意向量引擎（512 維）。
/// 首次使用時自動從 HuggingFace 下載模型與詞彙表，快取於
/// %LOCALAPPDATA%\WorkDiary\models\。
/// </summary>
public class EmbeddingService : IDisposable
{
    // ── 模型檔案路徑 ──
    public static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkDiary", "models");

    private static readonly string ModelPath =
        Path.Combine(ModelsDir, "bge-small-zh-v1.5.onnx");
    private static readonly string VocabPath =
        Path.Combine(ModelsDir, "bge-small-zh-v1.5-vocab.txt");

    private const string ModelUrl =
        "https://huggingface.co/BAAI/bge-small-zh-v1.5/resolve/main/onnx/model.onnx";
    private const string VocabUrl =
        "https://huggingface.co/BAAI/bge-small-zh-v1.5/resolve/main/vocab.txt";

    private const int MaxSeqLen = 512;

    /// <summary>向量維度（512）。可供外部偵測 DB 中舊維度的 BLOB 是否需重算。</summary>
    public const int EmbeddingDim = 512;

    // ── 狀態 ──
    private InferenceSession?         _session;
    private Dictionary<string, int>?  _vocab;
    private bool                      _isReady;

    public bool IsReady => _isReady;

    /// <summary>初始化進度通知（可能在非 UI 執行緒發出）。</summary>
    public event Action<string>? StatusChanged;

    // ════════════════════════════════════════
    // 初始化
    // ════════════════════════════════════════

    public async Task InitializeAsync()
    {
        if (_isReady) return;

        Directory.CreateDirectory(ModelsDir);

        if (!File.Exists(VocabPath))
        {
            StatusChanged?.Invoke("正在下載詞彙表...");
            await DownloadFileAsync(VocabUrl, VocabPath);
        }

        if (!File.Exists(ModelPath))
        {
            StatusChanged?.Invoke("正在下載 ONNX 模型（約 93MB）...");
            await DownloadFileAsync(ModelUrl, ModelPath);
        }

        StatusChanged?.Invoke("正在載入詞彙表...");
        _vocab = LoadVocab(VocabPath);

        StatusChanged?.Invoke("正在初始化 ONNX 推論引擎...");
        var opts = new SessionOptions();
        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        _session = new InferenceSession(ModelPath, opts);

        _isReady = true;
        StatusChanged?.Invoke("語意搜尋引擎就緒");
    }

    // ════════════════════════════════════════
    // 向量推論
    // ════════════════════════════════════════

    /// <summary>將文字轉換為 512 維 L2 正規化語意向量（CLS Token Pooling）。</summary>
    public float[] GetEmbedding(string text)
    {
        if (!_isReady || _session == null || _vocab == null)
            throw new InvalidOperationException("EmbeddingService 尚未初始化。");

        var (inputIds, attentionMask, tokenTypeIds) = Tokenize(text);
        int seqLen = inputIds.Length;

        var inputIdsTensor = CreateTensor(inputIds, seqLen);
        var attMaskTensor  = CreateTensor(attentionMask, seqLen);
        var tokTypeTensor  = CreateTensor(tokenTypeIds, seqLen);

        // 只傳入模型實際接受的輸入
        var inputNames = _session.InputMetadata.Keys.ToHashSet();
        var inputs = new List<NamedOnnxValue>();
        if (inputNames.Contains("input_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor));
        if (inputNames.Contains("attention_mask"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", attMaskTensor));
        if (inputNames.Contains("token_type_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokTypeTensor));

        using var results = _session.Run(inputs);

        // 取 last_hidden_state（或第一個輸出），shape [1, seqLen, dim]
        var outputName = _session.OutputMetadata.ContainsKey("last_hidden_state")
            ? "last_hidden_state"
            : _session.OutputMetadata.Keys.First();
        var hidden = results.First(r => r.Name == outputName).AsTensor<float>();
        int dim = (int)hidden.Dimensions[2];

        return ClsPoolAndNormalize(hidden, dim);
    }

    // ── CLS Token Pooling + L2 Normalize ──

    private static float[] ClsPoolAndNormalize(Tensor<float> hidden, int dim)
    {
        var vec = new float[dim];
        for (int d = 0; d < dim; d++) vec[d] = hidden[0, 0, d];

        float norm = 0f;
        for (int d = 0; d < dim; d++) norm += vec[d] * vec[d];
        norm = (float)Math.Sqrt(norm);

        if (norm > 1e-9f)
            for (int d = 0; d < dim; d++) vec[d] /= norm;

        return vec;
    }

    // ════════════════════════════════════════
    // 相似度
    // ════════════════════════════════════════

    /// <summary>餘弦相似度（兩向量均已 L2 正規化，故等同點積）。</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f;
        for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot;
    }

    // ════════════════════════════════════════
    // BERT WordPiece Tokenizer
    // ════════════════════════════════════════

    private (int[] ids, int[] mask, int[] typeIds) Tokenize(string text)
    {
        var words    = BasicTokenize(text);
        var tokenIds = new List<int>();

        foreach (var word in words)
        {
            tokenIds.AddRange(WordPieceTokenize(word));
            if (tokenIds.Count >= MaxSeqLen - 2) break;
        }

        if (tokenIds.Count > MaxSeqLen - 2)
            tokenIds = tokenIds.GetRange(0, MaxSeqLen - 2);

        int clsId = _vocab!.GetValueOrDefault("[CLS]", 101);
        int sepId = _vocab.GetValueOrDefault("[SEP]", 102);

        var ids     = new int[MaxSeqLen];
        var mask    = new int[MaxSeqLen];
        var typeIds = new int[MaxSeqLen]; // 全為 0

        ids[0]  = clsId;
        mask[0] = 1;

        for (int i = 0; i < tokenIds.Count; i++)
        {
            ids[i + 1]  = tokenIds[i];
            mask[i + 1] = 1;
        }

        ids[tokenIds.Count + 1]  = sepId;
        mask[tokenIds.Count + 1] = 1;

        return (ids, mask, typeIds);
    }

    /// <summary>僅保留繁/簡中文、英文、數字、空白及標點；過濾其他語言字元。</summary>
    private static bool IsKeepChar(char c) =>
        (c >= 'a' && c <= 'z') ||
        (c >= '0' && c <= '9') ||
        char.IsWhiteSpace(c)   ||
        IsChineseChar(c)       ||
        IsPunctuation(c);

    private List<string> BasicTokenize(string text)
    {
        text = text.ToLowerInvariant();

        // 中日韓字元前後插入空格，同時過濾不支援的字元集
        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            if (!IsKeepChar(c)) continue;
            if (IsChineseChar(c)) sb.Append(' ').Append(c).Append(' ');
            else sb.Append(c);
        }

        var tokens = new List<string>();
        foreach (var token in sb.ToString()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            tokens.AddRange(SplitOnPunctuation(token));

        return tokens;
    }

    private static IEnumerable<string> SplitOnPunctuation(string token)
    {
        var current = new StringBuilder();
        foreach (char c in token)
        {
            if (IsPunctuation(c))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                yield return c.ToString();
            }
            else current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private List<int> WordPieceTokenize(string token)
    {
        int unkId = _vocab!.GetValueOrDefault("[UNK]", 100);
        if (token.Length > 100) return new List<int> { unkId };

        var result = new List<int>();
        int start  = 0;

        while (start < token.Length)
        {
            int end    = token.Length;
            int bestId = -1;

            while (start < end)
            {
                var substr = start == 0 ? token[start..end] : "##" + token[start..end];
                if (_vocab.TryGetValue(substr, out int id)) { bestId = id; break; }
                end--;
            }

            if (bestId == -1) return new List<int> { unkId };
            result.Add(bestId);
            start = end;
        }

        return result;
    }

    // ════════════════════════════════════════
    // 工具方法
    // ════════════════════════════════════════

    private static DenseTensor<long> CreateTensor(int[] values, int seqLen)
    {
        var data = new long[seqLen];
        for (int i = 0; i < seqLen; i++) data[i] = values[i];
        return new DenseTensor<long>(data, new[] { 1, seqLen });
    }

    private static Dictionary<string, int> LoadVocab(string path)
    {
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        int id    = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
            vocab[line] = id++;
        return vocab;
    }

    private static async Task DownloadFileAsync(string url, string destPath)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var fs = File.Create(destPath);
        await response.Content.CopyToAsync(fs);
    }

    private static bool IsChineseChar(char c) =>
        (c >= '\u4E00' && c <= '\u9FFF') ||   // CJK 統一漢字
        (c >= '\u3400' && c <= '\u4DBF') ||   // CJK 擴充 A
        (c >= '\uF900' && c <= '\uFAFF');     // CJK 相容漢字

    private static bool IsPunctuation(char c)
    {
        if ((c >= '!' && c <= '/') || (c >= ':' && c <= '@') ||
            (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
            return true;

        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return cat is UnicodeCategory.OtherPunctuation
                   or UnicodeCategory.DashPunctuation
                   or UnicodeCategory.OpenPunctuation
                   or UnicodeCategory.ClosePunctuation
                   or UnicodeCategory.ConnectorPunctuation
                   or UnicodeCategory.InitialQuotePunctuation
                   or UnicodeCategory.FinalQuotePunctuation;
    }

    public void Dispose() => _session?.Dispose();
}
