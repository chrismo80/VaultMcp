using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class SearchNotesToolTests
{
    [Fact]
    public void Execute_returns_results_from_vault()
    {
        var results = new[]
        {
            new VaultSearchResult("workflows/invoice-flow.json", "Invoice Flow", "…Handles invoice correction…", 245)
        };

        var tool = new SearchNotesTool(new StubKnowledgeVault(
            new VaultStatus("/repo/docs/domain", true, 1, [".json"]),
            [],
            searchResults: results));

        var response = tool.Execute("invoice");

        response.Error.IsNull();
        response.Results.Count.Is(1);
        response.Results[0].Path.Is("workflows/invoice-flow.json");
        response.Results[0].Excerpt.Is("…Handles invoice correction…");
    }

    [Fact]
    public void Execute_returns_structured_error_when_vault_root_is_missing()
    {
        var tool = new SearchNotesTool(new StubKnowledgeVault(
            new VaultStatus("/repo/docs/domain", false, 0, [".json"]),
            []));

        var response = tool.Execute("invoice");

        response.Results.Count.Is(0);
        response.Error!.Message.Is("vault root not found");
    }
}
