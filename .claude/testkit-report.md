# TESTKIT 測試設計報告

> 產生時間：2026-03-06
> 專案：WorkDiary（C# WPF + SQLite + ONNX 語意搜尋桌面應用）
> Phase 1+2 輸出，待人工確認後執行 `/testkit --gen` 進入 Phase 3

---

## 技術棧

| 項目 | 內容 |
|---|---|
| 語言 | C# .NET 8 |
| 測試框架 | **xUnit** + **FluentAssertions** + **Moq** |
| DB 隔離 | SQLite In-Memory（`UseSqlite("DataSource=:memory;")`）|
| 特殊依賴 | ONNX Runtime（需真實模型），歸為 P2 整合測試 |

---

## 可測元件清單

| 元件 | 檔案 | 外部依賴 | 可測層級 |
|---|---|---|---|
| `DiaryService` | `Services/DiaryService.cs` | EF Core + SQLite | 單元（In-Memory DB）|
| `VectorStoreService` | `Services/VectorStoreService.cs` | 記憶體 + SQLite（持久化） | 純邏輯可直接測 |
| `EmbeddingService` | `Services/EmbeddingService.cs` | ONNX、HTTP、File System | 純函數單元 + ONNX 整合 |
| `FileService` | `Services/FileService.cs` | File System | 整合測試（需臨時目錄）|

---

## Decision Table

### DiaryService — SaveContentAsync

> 自動儲存：UPDATE 現有 / INSERT 新記錄 / 空白略過

| 日誌記錄存在？ | content 空白？ | 預期行為 |
|---|---|---|
| ✅ 存在 | 任意 | `ExecuteUpdate` 更新 Content 與 UpdatedAt |
| ❌ 不存在 | ❌ 非空 | INSERT 新 DiaryEntry |
| ❌ 不存在 | ✅ 空白 | 不寫入，DB 無異動 |

---

### DiaryService — GetOrCreateEntryAsync

| 日誌記錄存在？ | 預期行為 |
|---|---|
| ✅ 存在 | 回傳現有記錄（含 Attachments） |
| ❌ 不存在 | 建立新記錄，Date=date.Date，Content=""，回傳 |

---

### DiaryService — SearchAsync（快速搜尋，最多 10 筆）

| keyword 符合 Content？ | 符合 Tags？ | 置頂記錄存在？ | 預期行為 |
|---|---|---|---|
| ✅ | 任意 | 任意 | 回傳符合記錄，最多 10 筆 |
| ❌ | ✅ | 任意 | 回傳符合記錄，最多 10 筆 |
| ❌ | ❌ | 任意 | 回傳空清單 |
| ✅（符合 > 10 筆） | 任意 | 任意 | 只回傳 10 筆 |
| ✅ | 任意 | ✅ 置頂存在 | 置頂記錄排在最前 |
| `""` 空字串 | - | - | 回傳全部（LIKE `%%` 全符合），最多 10 筆 |

---

### DiaryService — SearchForBrowseAsync（瀏覽全文搜尋）

| keyword 符合 Content？ | 符合 Tags？ | 預期行為 |
|---|---|---|
| ✅ | 任意 | 回傳，置頂優先，日期降冪，**無 10 筆限制** |
| ❌ | ✅ | 回傳 |
| ❌ | ❌ | 空清單 |

---

### DiaryService — SyncTagsAsync

> 使用原生 SQL，junction table 操作

| 日誌存在？ | tagNames 內容 | 預期行為 |
|---|---|---|
| ❌ 不存在 | 任意 | 直接 return，無 DB 異動 |
| ✅ 存在 | 空清單 `[]` | 清除舊 junction records，不新增 |
| ✅ 存在 | `["A", "B"]` | 清除舊記錄，確保 Tag A/B 存在，建 junction |
| ✅ 存在 | `["A", "a"]`（大小寫重複） | 去重，只建一筆（`Distinct(OrdinalIgnoreCase)`）|
| ✅ 存在 | Tag 已存在於 Tags 表 | `INSERT OR IGNORE` 不重複建 Tag |

---

### DiaryService — SetPinnedAsync / SaveTagsAsync

| 日誌存在？ | 預期行為 |
|---|---|
| ✅ 存在 | 更新對應欄位 |
| ❌ 不存在 | `ExecuteUpdate` 回傳 0，無副作用 |

---

### VectorStoreService — SearchTopK

| 記憶體向量庫狀態 | k 值 | 預期行為 |
|---|---|---|
| 有 N 筆向量（N > k） | k | 回傳 k 筆，按 CosineSimilarity 降冪 |
| 有 N 筆向量（N < k） | k | 回傳全部 N 筆，不拋例外 |
| 空庫（0 筆） | 任意 | 回傳空清單 |
| 有向量，query 維度不符 | - | 計算時 `a[i] * b[i]` 越界 → 應確認行為（目前無驗證）|

---

### VectorStoreService — FloatToBlob / FloatFromBlob

| 輸入 | 預期行為 |
|---|---|
| `float[]` 長度 N | `FloatToBlob` 回傳 N×4 bytes |
| N×4 bytes BLOB | `FloatFromBlob` 回傳長度 N 的 float[] |
| 往返（ToBlob → FromBlob） | 數值完全還原（bit-exact）|
| 長度 0 的陣列 | 回傳空 byte[] / 空 float[]，不拋例外 |

---

### VectorStoreService — Upsert / Remove

| 操作 | entryId 已存在？ | 預期行為 |
|---|---|---|
| `Upsert` | ❌ | 新增 entry |
| `Upsert` | ✅ | 覆蓋舊向量 |
| `Remove` | ✅ | 移除，Count 減 1 |
| `Remove` | ❌ | 靜默無動作（Dictionary.Remove 不拋例外）|

---

### EmbeddingService — CosineSimilarity（靜態純函數）

| 向量狀態 | 預期行為 |
|---|---|
| 兩向量均已 L2 正規化 | 回傳值介於 -1.0 ~ 1.0 |
| 完全相同向量 | 回傳值 ≈ 1.0 |
| 正交向量 | 回傳值 ≈ 0.0 |
| 反向向量 | 回傳值 ≈ -1.0 |
| 零向量（norm=0）作為輸入 | 回傳 0.0（全 0 點積） |

---

### EmbeddingService — BasicTokenize / WordPieceTokenize（私有，透過 GetEmbedding 間接測試）

> 透過 reflection 或 internal 可見性對 Tokenize 進行單元測試

| 輸入類型 | 長度 | 預期行為 |
|---|---|---|
| 純中文（如「今天天氣好」） | 短 | 每個漢字獨立為 token，CJK char ids |
| 純英文（如 `Hello World`） | 短 | 小寫化，WordPiece 切分 |
| 中英混合 | 短 | 漢字空格分離 + 英文 WordPiece |
| 超長文字 | > 510 tokens | 截斷至 510，加 [CLS][SEP] 共 512 |
| 空字串 `""` | 0 | ids = [CLS, SEP, 0...0]，mask = [1,1,0...0] |
| 含不支援字元（如日文假名） | 短 | `IsKeepChar` 過濾，不拋例外 |
| 超長單詞（> 100 chars） | - | `WordPieceTokenize` 回傳 `[UNK]` |

---

### EmbeddingService — GetEmbedding（未初始化時）

| IsReady 狀態 | 預期行為 |
|---|---|
| `false`（未呼叫 InitializeAsync） | 拋出 `InvalidOperationException` |
| `true` | 正常回傳 512 維 float[]，L2 norm ≈ 1.0 |

---

### FileService — CopyToStorage

| 來源檔案存在？ | 同名衝突？ | 預期行為 |
|---|---|---|
| ✅ 存在 | ❌ 無衝突 | 複製到 `attachments/{date}/filename`，回傳相對路徑 |
| ✅ 存在 | ✅ 一次衝突 | 自動改名為 `filename_1.ext` |
| ✅ 存在 | ✅ N 次衝突 | 依序嘗試 `_1`, `_2`, ... `_N` |
| ❌ 不存在 | - | `File.Copy` 拋出 `FileNotFoundException` |

---

### FileService — DeleteFile

| 相對路徑對應檔案存在？ | 預期行為 |
|---|---|
| ✅ 存在 | 刪除實體檔案 |
| ❌ 不存在 | `File.Exists` 為 false，靜默不拋例外 |

---

## 測試策略

### DiaryService

- **隔離方式**：`UseSqlite("DataSource=:memory;")` In-Memory SQLite（比 `UseInMemoryDatabase` 更忠實 SQL 行為，特別是 `ExecuteSqlRawAsync` 需要真實 SQL 引擎）
- **Helper**：`DbContextFactory.cs` 提供每個測試獨立的 `AppDbContext`
- **Mock**：不需要，直接測真實 EF Core + SQLite

### VectorStoreService

- **隔離方式**：純記憶體，直接 `new VectorStoreService()` 即可
- `PersistAsync` / `LoadAllAsync` 需要 `AppDbContext`，用 In-Memory SQLite

### EmbeddingService

- **純函數**（`CosineSimilarity`、`ClsPoolAndNormalize`）：直接 `new EmbeddingService()` 呼叫 static 方法
- **Tokenizer**（`BasicTokenize`、`WordPieceTokenize`）：需 `_vocab`，可透過 reflection 或 `InternalsVisibleTo` 測試
- **`GetEmbedding` 整合**：需真實 ONNX 模型（93MB），標記 `[Trait("Category","Integration")]`，CI 排除

### FileService

- **隔離方式**：覆寫 `AttachmentsRoot` 或在測試中使用 `Path.GetTempPath()` 臨時目錄
- **Teardown**：每個測試結束後清理臨時目錄
- 歸為 P1（輕量 I/O，不需網路/DB）

---

## 優先順序

| 優先 | 測試對象 | 理由 |
|---|---|---|
| **P0** | `DiaryService.SaveContentAsync` | 核心寫入邏輯，INSERT/UPDATE/略過三路分支 |
| **P0** | `DiaryService.SearchAsync` | 使用者最常用，10 筆上限 + 置頂排序 |
| **P0** | `DiaryService.SearchForBrowseAsync` | 瀏覽模式主功能 |
| **P0** | `VectorStoreService.SearchTopK` | 語意搜尋正確性 |
| **P0** | `VectorStoreService.FloatToBlob/FloatFromBlob` | Embedding 持久化基礎，位元精確還原 |
| **P1** | `DiaryService.SyncTagsAsync` | 原生 SQL + junction table，最易出錯 |
| **P1** | `DiaryService.GetOrCreateEntryAsync` | 啟動時呼叫，建立/取回邏輯 |
| **P1** | `EmbeddingService.CosineSimilarity` | 靜態純函數，零成本高信心 |
| **P1** | `EmbeddingService.Tokenize`（透過反射） | WordPiece 截斷/UNK/空字串邊界 |
| **P1** | `FileService.CopyToStorage` | 衝突重命名邏輯，純本地 I/O |
| **P2** | `EmbeddingService.GetEmbedding` | 需 ONNX 模型（93MB），整合測試 |
| **P2** | `EmbeddingService.InitializeAsync` | 需 HTTP + File，整合測試 |

---

## 測試專案結構（Phase 3 預期輸出）

```
WorkDiary.Tests/
  WorkDiary.Tests.csproj          ← xUnit + FluentAssertions + Moq + Microsoft.EntityFrameworkCore.Sqlite
  Services/
    DiaryServiceTests.cs          ← P0+P1，In-Memory SQLite
    VectorStoreServiceTests.cs    ← P0+P1，純記憶體
    EmbeddingServiceTests.cs      ← P1 純函數，P2 ONNX 整合
    FileServiceTests.cs           ← P1，臨時目錄
  Helpers/
    DbContextFactory.cs           ← CreateInMemoryContext() 工廠方法
```

---

**TESTKIT Phase 1+2 完成。**

✅ 報告已寫入 `.claude/testkit-report.md`
請檢查 Decision Table，確認「測什麼」正確後，
執行 `/testkit --gen` 進行 Phase 3 程式碼生成。
