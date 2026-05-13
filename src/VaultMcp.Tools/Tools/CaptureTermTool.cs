using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record CaptureTermResponse(
    VaultTermCaptureResult? Result,
    ErrorInfo? Error = null)
{
    public static CaptureTermResponse AsError(ErrorInfo error) => new(null, error);
}

[McpServerToolType]
public sealed class CaptureTermTool(IVault vault)
{
    [McpServerTool(Name = "capture_term", Title = "Capture Term")]
    [Description("Create or refine a lexicon entry for a domain term or named concept. Use this for low-friction glossary growth, aliases, and primary group assignment without forcing the caller through the larger structured capture surface.")]
    public CaptureTermResponse Execute(
        [Description("Canonical term or concept name, for example 'Chargenfreigabe' or 'Invoice Export'.")]
        string term,
        [Description("Human-readable description of the term. This is the primary knowledge payload.")]
        string description,
        [Description("Optional alternate names for the same term.")]
        string[]? aliases = null,
        [Description("Optional primary group or category, for example 'Freigabearten'.")]
        string? group = null)
    {
        try
        {
            var result = vault.CaptureTerm(new VaultTermCapture(term, description, aliases ?? [], group));
            return new CaptureTermResponse(result);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or DirectoryNotFoundException or IOException)
        {
            return CaptureTermResponse.AsError(VaultToolErrors.FromException(exception));
        }
    }
}
