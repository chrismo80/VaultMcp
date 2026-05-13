using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase.SemanticIndex;
using VaultMcp.Tools.KnowledgeBase.Vault.Json;
using Xunit;

namespace VaultMcp.Tools.Tests.KnowledgeBase.SemanticIndex;

public sealed class JsonBinarySemanticIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VaultMcp.Semantic.Tests", Guid.NewGuid().ToString("N"));

    public JsonBinarySemanticIndexTests()
        => Directory.CreateDirectory(_root);

    [Fact]
    public void Rebuild_creates_index_files_and_semantic_search_returns_top_hit()
    {
        WriteNote("workflows/invoice-flow.json", "# Invoice Flow\n\nInvoice approval and payment release.");
        WriteNote("operations/shipping.json", "# Shipping\n\nParcel delivery and warehouse handling.");

        var index = CreateIndex(new KeywordEmbeddingProvider("test-embed-v1"));

        index.Rebuild();
        var results = index.Search("invoice payment", 5);
        var status = index.GetStatus();

        File.Exists(Path.Combine(_root, ".vault", "semantic-index.json")).IsTrue();
        File.Exists(Path.Combine(_root, ".vault", "semantic-vectors.bin")).IsTrue();
        File.Exists(Path.Combine(_root, ".vault", "index-state.json")).IsTrue();
        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("workflows", "invoice-flow.json"));
        status.IndexPresent.IsTrue();
        status.ChunkCount.Is(2);
        status.IndexedFileCount.Is(2);
    }

    [Fact]
    public void UpsertFile_replaces_changed_chunks_for_single_note()
    {
        var path = Path.Combine("workflows", "invoice-flow.json");
        WriteNote(path, "# Invoice Flow\n\nInvoice approval and payment release.");
        var index = CreateIndex(new KeywordEmbeddingProvider("test-embed-v1"));

        index.Rebuild();

        WriteNote(path, "# Invoice Flow\n\nWarehouse staging and package handling.");
        index.UpsertFile(path);

        var results = index.Search("warehouse package", 5);

        results[0].Path.Is(path);
        results[0].TextPreview.Contains("Warehouse staging", StringComparison.OrdinalIgnoreCase).IsTrue();
    }

    [Fact]
    public void UpsertFile_accepts_non_native_directory_separators()
    {
        var path = Path.Combine("workflows", "invoice-flow.json");
        WriteNote(path, "# Invoice Flow\n\nInvoice approval and payment release.");
        var index = CreateIndex(new KeywordEmbeddingProvider("test-embed-v1"));

        index.Rebuild();

        WriteNote(path, "# Invoice Flow\n\nWarehouse staging and package handling.");
        index.UpsertFile(WithForeignSeparators(path));

        var results = index.Search("warehouse package", 5);

        results[0].Path.Is(path);
        results[0].TextPreview.Contains("Warehouse staging", StringComparison.OrdinalIgnoreCase).IsTrue();
    }

    [Fact]
    public void DeleteFile_removes_chunks_from_index()
    {
        WriteNote(Path.Combine("workflows", "invoice-flow.json"), "# Invoice Flow\n\nInvoice approval and payment release.");
        WriteNote(Path.Combine("operations", "shipping.json"), "# Shipping\n\nParcel delivery and warehouse handling.");
        var index = CreateIndex(new KeywordEmbeddingProvider("test-embed-v1"));

        index.Rebuild();
        index.DeleteFile(Path.Combine("workflows", "invoice-flow.json"));

        var results = index.Search("invoice payment", 5);
        var status = index.GetStatus();

        results.Any(hit => string.Equals(hit.Path, Path.Combine("workflows", "invoice-flow.json"), StringComparison.OrdinalIgnoreCase)).IsFalse();
        status.IndexedFileCount.Is(1);
    }

    [Fact]
    public void UpsertFile_rejects_path_traversal()
    {
        var index = CreateIndex(new KeywordEmbeddingProvider("test-embed-v1"));

        var exception = Record.Exception(() => index.UpsertFile(Path.Combine("..", "secrets.json")));

        exception.IsNotNull();
        exception.Is<ArgumentException>();
        exception.Message.Contains("escapes the configured vault root", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void Search_requires_rebuild_when_embedding_model_changes()
    {
        WriteNote("workflows/invoice-flow.json", "# Invoice Flow\n\nInvoice approval and payment release.");
        CreateIndex(new KeywordEmbeddingProvider("test-embed-v1")).Rebuild();
        var index = CreateIndex(new KeywordEmbeddingProvider("test-embed-v2"));

        var exception = Record.Exception(() => index.Search("invoice", 5));

        exception.IsNotNull();
        exception.Is<SemanticIndexModelMismatchException>();
    }

    [Fact]
    public void Chunker_creates_stable_heading_based_chunk_ids()
    {
        var content = "# Invoice Flow\n\n## Overview\nInvoice approval and release.\n\n## Rules\nOnly approved invoices may ship.";

        var parsed = JsonVaultParser.Parse(BuildJsonNote(Path.Combine("workflows", "invoice-flow.json"), content), "invoice-flow");
        var chunks = VaultNoteChunker.Chunk(Path.Combine("workflows", "invoice-flow.json"), parsed, DateTimeOffset.UtcNow, 350, 240);

        chunks.Count.Is(2);
        chunks[0].Id.Is(Path.Combine("workflows", "invoice-flow.json#s0-overview:0"));
        chunks[1].Id.Is(Path.Combine("workflows", "invoice-flow.json#s1-rules:0"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private JsonBinarySemanticIndex CreateIndex(IEmbeddingProvider embeddingProvider)
        => new(
            new SemanticIndexOptions(
                _root,
                Path.Combine(_root, ".vault"),
                embeddingProvider.ProviderName,
                embeddingProvider.ModelName,
                string.Empty,
                string.Empty,
                350,
                240,
                SemanticSearchScoringOptions.Default),
            embeddingProvider);

    private void WriteNote(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, BuildJsonNote(relativePath, content));
    }

    private static string BuildJsonNote(string path, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var title = Path.GetFileNameWithoutExtension(path);
        var bodyLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal) && string.Equals(title, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal))
            {
                title = trimmed[2..].Trim();
                continue;
            }

            bodyLines.Add(line);
        }

        var summary = string.Join("\n", bodyLines).Trim();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = "vault-note/v1",
            title,
            summary,
            tags = Array.Empty<string>(),
            aliases = Array.Empty<string>(),
            related = Array.Empty<string>()
        });
    }

    private static string WithForeignSeparators(string path)
        => Path.DirectorySeparatorChar == '/'
            ? path.Replace('/', '\\')
            : path.Replace('\\', '/');

    private sealed class KeywordEmbeddingProvider(string modelName) : IEmbeddingProvider
    {
        public string ProviderName => "test";
        public string ModelName => modelName;
        public bool IsConfigured => true;

        public float[] Embed(string text)
        {
            var normalized = text.ToLowerInvariant();
            var finance = ContainsAny(normalized, "invoice", "payment", "billing", "approval") ? 1f : 0f;
            var logistics = ContainsAny(normalized, "warehouse", "shipping", "parcel", "package") ? 1f : 0f;
            var policy = ContainsAny(normalized, "rule", "policy", "approved") ? 1f : 0.1f;
            return [finance, logistics, policy];
        }

        private static bool ContainsAny(string text, params string[] values)
            => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}
