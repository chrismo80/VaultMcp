using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record FindRelatedNotesResponse(
    IReadOnlyList<VaultMatch> Results,
    ErrorInfo? Error = null)
{
    public static FindRelatedNotesResponse AsError(ErrorInfo error) => new([], error);
}

[McpServerToolType]
public sealed class FindRelatedNotesTool(IVault vault)
{
    [McpServerTool(Name = "find_related_notes", Title = "Find Related Notes")]
    [Description("Find notes related to a given vault note using shared domain terms, tags, explicit related links, and directory proximity. Use this after `get_note` when you need broader domain context around a relevant note.")]
    public FindRelatedNotesResponse Execute(
        [Description("Vault-relative note path, for example 'workflows/invoice-flow.json'.")]
        string path,
        [Description("Maximum number of related notes to return. Default: 3.")]
        int maxCount = 3)
    {
        if (VaultToolErrors.ValidateReadableVault(vault) is { } vaultError)
            return FindRelatedNotesResponse.AsError(vaultError);

        try
        {
            return new FindRelatedNotesResponse(VaultToolPayloads.FromSearchResults(vault.FindRelatedNotes(path, maxCount)));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return FindRelatedNotesResponse.AsError(VaultToolErrors.FromException(exception));
        }
    }
}
