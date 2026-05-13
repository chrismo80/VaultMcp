namespace VaultMcp.Tools.KnowledgeBase.Vault;

public interface IVault
{
    VaultStatus GetStatus();
    IReadOnlyList<VaultNote> ListNotes(int maxCount = 100);
    VaultNoteDocument GetNote(string relativePath, int maxChars = 12000);
    IReadOnlyList<VaultSearchResult> SearchNotes(string query, int maxCount = 10);
    IReadOnlyList<VaultSearchResult> FindTerm(string term, int maxCount = 10);
    IReadOnlyList<VaultSearchResult> FindRelatedNotes(string relativePath, int maxCount = 5);
    VaultCaptureResult CaptureLearning(VaultLearningCapture learning);
    VaultTermCaptureResult CaptureTerm(VaultTermCapture term);
}
