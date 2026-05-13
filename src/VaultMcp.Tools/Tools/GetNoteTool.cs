using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record GetNoteResponse(
    VaultContextNote? Note,
    ErrorInfo? Error = null)
{
    public static GetNoteResponse AsError(ErrorInfo error) => new(null, error);
}

[McpServerToolType]
public sealed class GetNoteTool(IVault vault)
{
    [McpServerTool(Name = "get_note", Title = "Get Note")]
    [Description("Load a structured note from the configured vault by relative path. Use this after `find_term` or `search_notes` to read the actual stored knowledge before continuing work.")]
    public GetNoteResponse Execute(
        [Description("Vault-relative note path, for example 'glossary/order.json'.")]
        string path,
        [Description("Maximum number of characters to return from the note content. Default: 4000.")]
        int maxChars = 4000)
    {
        if (VaultToolErrors.ValidateReadableVault(vault) is { } vaultError)
            return GetNoteResponse.AsError(vaultError);

        try
        {
            return new GetNoteResponse(VaultToolPayloads.FromDocument(vault.GetNote(path, maxChars)));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return GetNoteResponse.AsError(VaultToolErrors.FromException(exception));
        }
    }
}
