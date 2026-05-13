using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.SemanticIndex;
using VaultMcp.Tools.KnowledgeBase.Search;
using VaultMcp.Tools.KnowledgeBase.Search.Lexical;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class SemanticIndexToolsTests
{
    private static readonly SemanticIndexStatus ReadyStatus = new(
        "/repo/docs/domain",
        "/repo/docs/domain/.vault",
        true,
        "test",
        "test-embed-v1",
        true,
        "test-embed-v1",
        3,
        2,
        1,
        DateTimeOffset.UtcNow);

    [Fact]
    public void SemanticSearchNotes_returns_structured_error_when_index_is_missing()
    {
        var vault = new StubKnowledgeVault(new VaultStatus("/repo/docs/domain", true, 1, [".json"]), []);
        var tool = new SemanticSearchNotesTool(vault, new StubSemanticIndex(ReadyStatus, searchException: new SemanticIndexNotBuiltException("semantic index not built. Call reindex_vault first.")));

        var response = tool.Execute("invoice");

        response.Results.Count.Is(0);
        response.Error!.Message.Is("semantic index not built");
    }

    [Fact]
    public void ReindexVault_returns_current_status_after_rebuild()
    {
        var semanticIndex = new StubSemanticIndex(ReadyStatus);
        var tool = new ReindexVaultTool(new StubKnowledgeVault(new VaultStatus("/repo/docs/domain", true, 1, [".json"]), []), semanticIndex);

        var response = tool.Execute();

        response.Error.IsNull();
        response.Status!.ChunkCount.Is(2);
        semanticIndex.RebuildCalls.Is(1);
    }

    [Fact]
    public void CaptureLearning_reindexes_affected_file_when_semantic_index_is_available()
    {
        var stubVault = new StubKnowledgeVault(
            new VaultStatus("/repo/docs/domain", true, 1, [".json"]),
            [],
            captureResult: new VaultCaptureResult("glossary/order.json", "Order", "term", true, false, false, "Created."));
        var semanticIndex = new StubSemanticIndex(ReadyStatus);
        var tool = new CaptureLearningTool(stubVault, semanticIndex);

        var response = tool.Execute("term", "Order", "Canonical order term.");

        response.Error.IsNull();
        response.IndexError.IsNull();
        semanticIndex.LastUpsertPath.Is("glossary/order.json");
    }

    [Fact]
    public void RecallContext_includes_semantic_matches_without_breaking_lexical_flow()
    {
        var semanticResults = new[]
        {
            new SemanticSearchHit("chunk-1", "workflows/order-flow.json", "Order Flow", "Overview", 0.92f, "Order flow preview")
        };
        var termResults = new[]
        {
            new VaultSearchResult("glossary/order.json", "Order", "# Order", 1200)
        };
        var searchResults = new[]
        {
            new VaultSearchResult("workflows/order-flow.json", "Order Flow", "…order flow…", 320)
        };
        var documents = new Dictionary<string, VaultNoteDocument>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflows/order-flow.json"] = new("workflows/order-flow.json", "Order Flow", "# Order Flow\n\nStep 1"),
            ["glossary/order.json"] = new("glossary/order.json", "Order", "# Order\n\nCanonical term")
        };

        var tool = new RecallContextTool(
            new StubKnowledgeVault(new VaultStatus("/repo/docs/domain", true, 2, [".json"]), [], termResults: termResults, searchResults: searchResults, documentsByPath: documents),
            new StubSemanticIndex(ReadyStatus, semanticResults));

        var response = tool.Execute("order");

        response.Error.IsNull();
        response.SemanticError.IsNull();
        response.SemanticMatches.Count.Is(1);
        response.SemanticMatches[0].Path.Is("workflows/order-flow.json");
        response.Matches.Count.Is(2);
        response.Notes[0].Path.Is("workflows/order-flow.json");
    }

    [Fact]
    public void AddVaultMcp_keeps_lexical_search_as_default_search_service()
    {
        var services = new ServiceCollection();

        services.AddVaultMcp("/tmp/vault");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISearch>().GetType().Is(typeof(LexicalSearch));
    }

    [Fact]
    public void AddVaultMcp_defaults_semantic_model_to_all_minilm_and_uses_bundled_provider_when_available()
    {
        using var _ = new SemanticIndexEnvironmentScope();
        var services = new ServiceCollection();

        services.AddVaultMcp("/tmp/vault");

        using var provider = services.BuildServiceProvider();
        var status = provider.GetRequiredService<ISemanticIndex>().GetStatus();

        status.ProviderConfigured.IsTrue();
        status.ProviderName.Is("onnx");
        status.ConfiguredEmbeddingModel.Is("all-MiniLM-L6-v2");
    }

    [Fact]
    public void AddVaultMcp_uses_local_onnx_provider_when_assets_exist_in_default_location()
    {
        using var root = new SemanticIndexTestDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".vault", "models", "all-MiniLM-L6-v2", "onnx"));
        File.WriteAllText(Path.Combine(root.Path, ".vault", "models", "all-MiniLM-L6-v2", "onnx", "model_qint8_arm64.onnx"), string.Empty);
        File.WriteAllText(Path.Combine(root.Path, ".vault", "models", "all-MiniLM-L6-v2", "vocab.txt"), string.Empty);

        using var _ = new SemanticIndexEnvironmentScope();
        var services = new ServiceCollection();

        services.AddVaultMcp(root.Path);

        using var provider = services.BuildServiceProvider();
        var embeddingProvider = provider.GetRequiredService<IEmbeddingProvider>();

        embeddingProvider.ProviderName.Is("onnx");
        embeddingProvider.ModelName.Is("all-MiniLM-L6-v2");
        embeddingProvider.IsConfigured.IsTrue();
    }
}

internal sealed class SemanticIndexEnvironmentScope : IDisposable
{
    private readonly string? _provider = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_PROVIDER");
    private readonly string? _model = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL");
    private readonly string? _modelPath = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL_PATH");
    private readonly string? _vocabPath = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_VOCAB_PATH");

    public SemanticIndexEnvironmentScope(string? provider = null, string? model = null, string? modelPath = null, string? vocabPath = null)
    {
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_PROVIDER", provider);
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL", model);
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL_PATH", modelPath);
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_VOCAB_PATH", vocabPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_PROVIDER", _provider);
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL", _model);
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL_PATH", _modelPath);
        Environment.SetEnvironmentVariable("VAULTMCP_EMBEDDINGS_VOCAB_PATH", _vocabPath);
    }
}

internal sealed class SemanticIndexTestDirectory : IDisposable
{
    public SemanticIndexTestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VaultMcp.SemanticIndex.TestDir", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
