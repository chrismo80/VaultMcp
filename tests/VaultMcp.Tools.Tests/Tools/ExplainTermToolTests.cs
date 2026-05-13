using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class ExplainTermToolTests
{
    [Fact]
    public void Execute_returns_lexicon_style_response()
    {
        var status = new VaultStatus("/repo/docs/domain", true, 3, [".json"]);
        var notes = new[]
        {
            new VaultNote("glossary/chargenfreigabe.json", "Chargenfreigabe"),
            new VaultNote("glossary/sperrlager.json", "Sperrlager"),
            new VaultNote("glossary/ruestfreigabe.json", "Rüstfreigabe")
        };
        var structured = new VaultStructuredContent(
            Scalars: new Dictionary<string, string> { ["group"] = "Freigabearten" });
        var documents = new Dictionary<string, VaultNoteDocument>
        {
            ["glossary/chargenfreigabe.json"] = new(
                "glossary/chargenfreigabe.json",
                "Chargenfreigabe",
                "# Chargenfreigabe",
                Kind: "term",
                Aliases: ["Batch Release"],
                Structured: structured,
                Summary: "Fachliche Freigabe einer Charge.",
                Details: "Fachliche Freigabe einer Charge. Sie folgt auf Qualitätsprüfung und steht im Kontrast zu Sperrlager. Rüstfreigabe ist ein Nachbarbegriff."),
            ["glossary/sperrlager.json"] = new(
                "glossary/sperrlager.json",
                "Sperrlager",
                "# Sperrlager",
                Kind: "term",
                Summary: "Blockierter Lagerzustand."),
            ["glossary/ruestfreigabe.json"] = new(
                "glossary/ruestfreigabe.json",
                "Rüstfreigabe",
                "# Rüstfreigabe",
                Kind: "term",
                Structured: structured,
                Summary: "Freigabe zum Produktionsstart.")
        };
        var termResults = new[]
        {
            new VaultSearchResult("glossary/chargenfreigabe.json", "Chargenfreigabe", "", 1000, "term")
        };
        var vault = new StubKnowledgeVault(status, notes, termResults: termResults, documentsByPath: documents);
        var tool = new ExplainTermTool(vault);

        var result = tool.Execute("Chargenfreigabe");

        result.Error.IsNull();
        result.Term.Is("Chargenfreigabe");
        result.Group.Is("Freigabearten");
        result.Aliases!.Single().Is("Batch Release");
        result.Mentions.Contains("Sperrlager", StringComparer.OrdinalIgnoreCase).IsTrue();
        result.SeeAlso.Contains("Rüstfreigabe", StringComparer.OrdinalIgnoreCase).IsTrue();
    }
}
