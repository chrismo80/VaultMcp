using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools.KnowledgeBase.SemanticIndex;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record ReindexVaultResponse(
    SemanticIndexStatus? Status,
    ErrorInfo? Error = null)
{
    public static ReindexVaultResponse AsError(ErrorInfo error) => new(null, error);
}

[McpServerToolType]
public sealed class ReindexVaultTool(IVault vault, ISemanticIndex semanticIndex)
{
    [McpServerTool(Name = "reindex_vault", Title = "Reindex Vault")]
    [Description("Maintenance tool that rebuilds the semantic index from structured source files under the configured vault root. Use only when semantic retrieval is unavailable, stale, broken, after bulk vault changes, or when explicitly requested.")]
    public ReindexVaultResponse Execute()
    {
        if (VaultToolErrors.ValidateReadableVault(vault) is { } vaultError)
            return ReindexVaultResponse.AsError(vaultError);

        try
        {
            semanticIndex.Rebuild();
            return new ReindexVaultResponse(semanticIndex.GetStatus());
        }
        catch (Exception exception) when (exception is IOException or SemanticIndexException)
        {
            return ReindexVaultResponse.AsError(VaultToolErrors.FromException(exception));
        }
    }
}
