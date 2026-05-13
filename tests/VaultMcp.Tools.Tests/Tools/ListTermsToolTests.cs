using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class ListTermsToolTests
{
    [Fact]
    public void Execute_lists_terms_by_group()
    {
        var structured = new VaultStructuredContent(Scalars: new Dictionary<string, string> { ["group"] = "Freigabearten" });
        var documents = new Dictionary<string, VaultNoteDocument>
        {
            ["glossary/chargenfreigabe.json"] = new("glossary/chargenfreigabe.json", "Chargenfreigabe", "# Chargenfreigabe", Kind: "term", Structured: structured, Summary: "Freigabe einer Charge."),
            ["glossary/ruestfreigabe.json"] = new("glossary/ruestfreigabe.json", "Rüstfreigabe", "# Rüstfreigabe", Kind: "term", Structured: structured, Summary: "Freigabe zum Produktionsstart.")
        };
        var notes = new[]
        {
            new VaultNote("glossary/chargenfreigabe.json", "Chargenfreigabe"),
            new VaultNote("glossary/ruestfreigabe.json", "Rüstfreigabe")
        };
        var tool = new ListTermsTool(new StubKnowledgeVault(new VaultStatus("/repo/docs/domain", true, 2, [".json"]), notes, documentsByPath: documents));

        var result = tool.Execute(group: "Freigabearten");

        result.Error.IsNull();
        result.Terms.Count.Is(2);
        result.Terms[0].Group.Is("Freigabearten");
    }
}
