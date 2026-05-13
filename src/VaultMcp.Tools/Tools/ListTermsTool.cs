using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record ListTermsResponse(
    string? Group,
    string? Query,
    IReadOnlyList<LexiconTermSummary> Terms,
    ErrorInfo? Error = null)
{
    public static ListTermsResponse AsError(string? group, string? query, ErrorInfo error)
        => new(group, query, [], error);
}

[McpServerToolType]
public sealed class ListTermsTool(IVault vault)
{
    [McpServerTool(Name = "list_terms", Title = "List Terms")]
    [Description("List named terms or concepts from the vault by group or by a category-style query. Use this when the user asks which terms exist in one part of the domain.")]
    public ListTermsResponse Execute(
        [Description("Optional explicit group name, for example 'Freigabearten'.")]
        string? group = null,
        [Description("Optional category-like query, for example 'Auftragsarten'. Use this when no explicit group is known.")]
        string? query = null,
        [Description("Maximum number of terms to return. Default: 10.")]
        int maxCount = 10)
    {
        if (VaultToolErrors.ValidateReadableVault(vault) is { } vaultError)
            return ListTermsResponse.AsError(group, query, vaultError);

        try
        {
            if (string.IsNullOrWhiteSpace(group) && string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Either group or query must be provided.");
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");

            if (!string.IsNullOrWhiteSpace(group))
            {
                var groupTerms = LexiconToolSupport.LoadNotes(vault)
                    .Where(note => string.Equals(LexiconToolSupport.TryGetGroup(note), group, StringComparison.OrdinalIgnoreCase))
                    .Select(LexiconToolSupport.ToTermSummary)
                    .OrderBy(item => item.Term, StringComparer.OrdinalIgnoreCase)
                    .Take(maxCount)
                    .ToArray();

                return new ListTermsResponse(group, query, groupTerms);
            }

            var resultTerms = vault.SearchNotes(query!, maxCount * 3)
                .Select(match => vault.GetNote(match.Path, 2000))
                .Select(LexiconToolSupport.ToTermSummary)
                .GroupBy(item => item.Path ?? item.Term, StringComparer.OrdinalIgnoreCase)
                .Select(grouped => grouped.First())
                .Take(maxCount)
                .ToArray();

            return new ListTermsResponse(group, query, resultTerms);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return ListTermsResponse.AsError(group, query, VaultToolErrors.FromException(exception));
        }
    }
}
