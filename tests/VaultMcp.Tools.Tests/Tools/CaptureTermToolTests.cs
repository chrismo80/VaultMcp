using Is.Assertions;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.Tools;
using Xunit;

namespace VaultMcp.Tools.Tests.Tools;

public sealed class CaptureTermToolTests
{
    [Fact]
    public void Execute_passes_term_capture_to_vault()
    {
        var captureResult = new VaultTermCaptureResult("glossary/chargenfreigabe.json", "Chargenfreigabe", true, false, false, "Created", ["Batch Release"], "Freigabearten");
        var stub = new StubKnowledgeVault(
            new VaultStatus("/repo/docs/domain", true, 0, [".json"]),
            [],
            captureTermResult: captureResult);
        var tool = new CaptureTermTool(stub);

        var result = tool.Execute("Chargenfreigabe", "Fachliche Freigabe einer Charge.", ["Batch Release"], "Freigabearten");

        result.Error.IsNull();
        result.Result!.Path.Is("glossary/chargenfreigabe.json");
        stub.LastCaptureTerm.IsNotNull();
        stub.LastCaptureTerm!.Term.Is("Chargenfreigabe");
        stub.LastCaptureTerm.Aliases!.Single().Is("Batch Release");
        stub.LastCaptureTerm.Group.Is("Freigabearten");
    }
}
