using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.SemanticIndex;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.KnowledgeBase.Vault.Json;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class CaptureLearningToolTests
{
    [Fact]
    public void Execute_returns_capture_result_from_vault()
    {
        var captureResult = new VaultCaptureResult("glossary/order.json", "Order", "term", true, false, false, "Created new knowledge note.");
        var stub = new StubKnowledgeVault(
            new VaultStatus("/repo/docs/domain", true, 1, [".json"]),
            [],
            captureResult: captureResult);
        var tool = new CaptureLearningTool(stub);

        var result = tool.Execute(
            "term",
            "Order",
            "Canonical order term.",
            "Extra detail",
            ["sales"],
            aliases: ["Sales Order", "Customer Order"],
            examples: ["Used in checkout", "Used in invoice creation"]);

        result.Error.IsNull();
        result.Result!.Path.Is("glossary/order.json");
        result.Result.Kind.Is("term");
        result.Result.Created.IsTrue();

        stub.LastCaptureLearning.IsNotNull();
        stub.LastCaptureLearning!.Aliases!.Count.Is(2);
        stub.LastCaptureLearning.Examples!.Count.Is(2);
    }

    [Fact]
    public void Execute_ignores_unavailable_semantic_provider_for_successful_capture()
    {
        var captureResult = new VaultCaptureResult("glossary/order.json", "Order", "term", true, false, false, "Created new knowledge note.");
        var stub = new StubKnowledgeVault(
            new VaultStatus("/repo/docs/domain", true, 1, [".json"]),
            [],
            captureResult: captureResult);
        var semanticIndex = new StubSemanticIndex(
            new SemanticIndexStatus("/repo/docs/domain", "/repo/docs/domain/.vault", false, "none", "all-MiniLM-L6-v2", false, null, null, 0, 0, null),
            upsertException: new EmbeddingProviderUnavailableException("embedding provider unavailable"));
        var tool = new CaptureLearningTool(stub, semanticIndex);

        var result = tool.Execute("term", "Order", "Canonical order term.");

        result.Error.IsNull();
        result.Result!.Created.IsTrue();
        result.IndexError.IsNull();
        semanticIndex.LastUpsertPath.Is("glossary/order.json");
    }

    [Fact]
    public void Execute_returns_structured_error_for_invalid_kind()
    {
        var root = Path.Combine(Path.GetTempPath(), "VaultMcp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var tool = new CaptureLearningTool(new JsonVault(root));

            var result = tool.Execute("invalid-kind", "Order", "Summary");

            result.Result.IsNull();
            result.Error.IsNotNull();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
