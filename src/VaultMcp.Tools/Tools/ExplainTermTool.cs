using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record ExplainTermResponse(
    string Term,
    string Summary,
    string Description,
    string? Group,
    string Kind,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Mentions,
    IReadOnlyList<string> SeeAlso,
    IReadOnlyList<string> NextQuestions,
    string? Path = null,
    ErrorInfo? Error = null)
{
    public static ExplainTermResponse AsError(string term, ErrorInfo error)
        => new(term, string.Empty, string.Empty, null, string.Empty, [], [], [], [], null, error);
}

[McpServerToolType]
public sealed class ExplainTermTool(IVault vault)
{
    [McpServerTool(Name = "explain_term", Title = "Explain Term")]
    [Description("Explain a domain term or named concept from the vault in a lexicon-style response. Use this when the user or agent needs the meaning, nearby concepts, and likely next questions instead of a raw note dump.")]
    public ExplainTermResponse Execute(
        [Description("Domain term or named concept to explain, for example 'Chargenfreigabe' or 'Invoice Export'.")]
        string term)
    {
        if (VaultToolErrors.ValidateReadableVault(vault) is { } vaultError)
            return ExplainTermResponse.AsError(term, vaultError);

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(term);
            var entry = LexiconToolSupport.Explain(vault, term);
            if (entry is null)
                return ExplainTermResponse.AsError(term, new ErrorInfo("term not found", new Dictionary<string, string> { ["term"] = term }));

            return new ExplainTermResponse(
                entry.Term,
                entry.Summary,
                entry.Description,
                entry.Group,
                entry.Kind,
                entry.Aliases,
                entry.Mentions,
                entry.SeeAlso,
                entry.NextQuestions,
                entry.Path);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return ExplainTermResponse.AsError(term, VaultToolErrors.FromException(exception));
        }
    }
}
