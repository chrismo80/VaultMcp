using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tests.Tools;

internal sealed class StubKnowledgeVault(
    VaultStatus status,
    IReadOnlyList<VaultNote> notes,
    VaultNoteDocument? document = null,
    IReadOnlyList<VaultSearchResult>? searchResults = null,
    IReadOnlyList<VaultSearchResult>? termResults = null,
    IReadOnlyList<VaultSearchResult>? relatedResults = null,
    VaultCaptureResult? captureResult = null,
    VaultTermCaptureResult? captureTermResult = null,
    IReadOnlyDictionary<string, VaultNoteDocument>? documentsByPath = null) : IVault
{
    public VaultLearningCapture? LastCaptureLearning { get; private set; }
    public VaultTermCapture? LastCaptureTerm { get; private set; }

    public VaultStatus GetStatus() => status;

    public IReadOnlyList<VaultNote> ListNotes(int maxCount = 100) => notes.Take(maxCount).ToArray();

    public int? LastGetNoteMaxChars { get; private set; }

    public VaultNoteDocument GetNote(string relativePath, int maxChars = 12000)
    {
        LastGetNoteMaxChars = maxChars;

        if (documentsByPath is not null && documentsByPath.TryGetValue(relativePath, out var configuredDocument))
            return configuredDocument;

        return document ?? throw new FileNotFoundException("Stub note not configured.", relativePath);
    }

    public IReadOnlyList<VaultSearchResult> SearchNotes(string query, int maxCount = 10) => (searchResults ?? []).Take(maxCount).ToArray();

    public IReadOnlyList<VaultSearchResult> FindTerm(string term, int maxCount = 10) => (termResults ?? []).Take(maxCount).ToArray();

    public IReadOnlyList<VaultSearchResult> FindRelatedNotes(string relativePath, int maxCount = 5) => (relatedResults ?? []).Take(maxCount).ToArray();

    public VaultCaptureResult CaptureLearning(VaultLearningCapture learning)
    {
        LastCaptureLearning = learning;
        return captureResult ?? new VaultCaptureResult("glossary/stub.json", learning.Title, learning.Kind, true, false, false, "Stub capture");
    }

    public VaultTermCaptureResult CaptureTerm(VaultTermCapture term)
    {
        LastCaptureTerm = term;
        return captureTermResult ?? new VaultTermCaptureResult("glossary/stub.json", term.Term, true, false, false, "Stub term capture", term.Aliases, term.Group);
    }
}
