using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record ComparedTerm(
    string Term,
    string OneLine,
    string? Group = null,
    string? Kind = null);

public sealed record CompareTermsResponse(
    string Topic,
    string CommonGround,
    IReadOnlyList<ComparedTerm> Differences,
    IReadOnlyList<string> ConfusionRisks,
    ErrorInfo? Error = null)
{
    public static CompareTermsResponse AsError(string topic, ErrorInfo error)
        => new(topic, string.Empty, [], [], error);
}

[McpServerToolType]
public sealed class CompareTermsTool(IVault vault)
{
    [McpServerTool(Name = "compare_terms", Title = "Compare Terms")]
    [Description("Compare multiple domain terms or named concepts side by side. Use this when similar names are easy to confuse or when the user asks for differences between concepts.")]
    public CompareTermsResponse Execute(
        [Description("Terms to compare, for example ['Rüstfreigabe', 'Chargenfreigabe'].")]
        string[] terms)
    {
        if (VaultToolErrors.ValidateReadableVault(vault) is { } vaultError)
            return CompareTermsResponse.AsError("comparison", vaultError);

        try
        {
            if (terms is null || terms.Length < 2)
                throw new ArgumentException("At least two terms are required for comparison.", nameof(terms));

            var entries = terms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => LexiconToolSupport.Explain(vault, term))
                .Where(entry => entry is not null)
                .Cast<LexiconEntry>()
                .ToArray();

            if (entries.Length < 2)
                return CompareTermsResponse.AsError("comparison", new ErrorInfo("not enough terms found", new Dictionary<string, string> { ["required"] = "2" }));

            var commonGroup = entries
                .Select(entry => entry.Group)
                .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group) && entries.All(entry => string.Equals(entry.Group, group, StringComparison.OrdinalIgnoreCase)));
            var topic = commonGroup ?? "Term Comparison";
            var commonGround = commonGroup is not null
                ? $"Alle Begriffe gehören zur Gruppe '{commonGroup}', beschreiben aber unterschiedliche fachliche Rollen oder Blickwinkel."
                : "Alle Begriffe liegen nah beieinander, unterscheiden sich aber in Bedeutung, Scope oder Einsatzkontext.";

            var differences = entries
                .Select(entry => new ComparedTerm(entry.Term, entry.Summary, entry.Group, entry.Kind))
                .ToArray();

            var confusionRisks = entries
                .SelectMany(entry => entry.SeeAlso.Contains(entry.Term, StringComparer.OrdinalIgnoreCase)
                    ? [$"{entry.Term} wird leicht verwechselt"]
                    : Array.Empty<string>())
                .Concat(entries.Select(entry => entry.Group).Where(group => !string.IsNullOrWhiteSpace(group)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                    ? ["ähnlicher Themenraum / ähnliche Wortfamilie"]
                    : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new CompareTermsResponse(topic, commonGround, differences, confusionRisks);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return CompareTermsResponse.AsError("comparison", VaultToolErrors.FromException(exception));
        }
    }
}
