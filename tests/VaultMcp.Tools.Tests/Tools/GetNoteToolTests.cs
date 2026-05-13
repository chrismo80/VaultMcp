using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class GetNoteToolTests
{
    [Fact]
    public void Execute_returns_document_from_vault()
    {
        var structured = new VaultStructuredContent(
            Scalars: new Dictionary<string, string> { ["source"] = "ERP" },
            Lists: new Dictionary<string, IReadOnlyList<string>> { ["steps"] = ["Validate", "Persist"] });
        var document = new VaultNoteDocument(
            "glossary/order.json",
            "Order",
            "# Order\n\nBody",
            Kind: "term",
            Aliases: ["Purchase Order"],
            Structured: structured);
        var stub = new StubKnowledgeVault(new VaultStatus("/repo/docs/domain", true, 1, [".json"]), [], document);
        var tool = new GetNoteTool(stub);

        var result = tool.Execute("glossary/order.json", 4000);

        result.Error.IsNull();
        result.Note!.Path.Is("glossary/order.json");
        result.Note.Title.Is("Order");
        result.Note.Content.Is("# Order\n\nBody");
        result.Note.Kind.Is("term");
        result.Note.Aliases!.Single().Is("Purchase Order");
        result.Note.Structured!.Scalars!["source"].Is("ERP");
        result.Note.Structured.Lists!["steps"].Count.Is(2);
        stub.LastGetNoteMaxChars.Is(4000);
    }

    [Fact]
    public void Execute_returns_structured_error_when_note_is_missing()
    {
        var tool = new GetNoteTool(new StubKnowledgeVault(new VaultStatus("/repo/docs/domain", true, 0, [".json"]), [], document: null));

        var result = tool.Execute("glossary/missing.json");

        result.Note.IsNull();
        result.Error!.Message.Is("note not found");
    }
}
