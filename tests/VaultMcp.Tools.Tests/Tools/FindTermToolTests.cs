using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class FindTermToolTests
{
    [Fact]
    public void Execute_returns_results_from_vault()
    {
        var results = new[]
        {
            new VaultSearchResult("glossary/order.json", "Order", "# Order", 1200)
        };

        var tool = new FindTermTool(new StubKnowledgeVault(
            new VaultStatus("/repo/docs/domain", true, 1, [".json"]),
            [],
            termResults: results));

        var response = tool.Execute("order");

        response.Error.IsNull();
        response.Results.Count.Is(1);
        response.Results[0].Title.Is("Order");
        response.Results[0].Kind.IsNull();
    }
}
