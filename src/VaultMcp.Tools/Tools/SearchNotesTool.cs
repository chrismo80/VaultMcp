using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record SearchNotesResponse(
    IReadOnlyList<VaultMatch> Results,
    ErrorInfo? Error = null)
{
    public static SearchNotesResponse AsError(ErrorInfo error) => new([], error);
}

[McpServerToolType]
public sealed class SearchNotesTool(IVault vault)
{
    [McpServerTool(Name = "search_notes", Title = "Search Notes")]
    [Description("Search structured vault notes lexically by title, path, metadata, and rendered content. Use this first for architecture, workflow, rule, or concept questions before asking the user again.")]
    public SearchNotesResponse Execute(
        [Description("Search query, for example a workflow name, business rule, invariant, or subsystem concept.")]
        string query,
        [Description("Maximum number of results to return. Default: 5.")]
        int maxCount = 5)
    {
        if (VaultToolErrors.ValidateReadableVault(vault) is { } vaultError)
            return SearchNotesResponse.AsError(vaultError);

        try
        {
            return new SearchNotesResponse(VaultToolPayloads.FromSearchResults(vault.SearchNotes(query, maxCount)));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or DirectoryNotFoundException or IOException)
        {
            return SearchNotesResponse.AsError(VaultToolErrors.FromException(exception));
        }
    }
}
