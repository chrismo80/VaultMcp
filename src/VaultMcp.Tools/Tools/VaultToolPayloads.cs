using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Search.Lexical;
using VaultMcp.Tools.KnowledgeBase.SemanticIndex;

namespace VaultMcp.Tools.Tools;

public sealed record VaultMatch(
    string Path,
    string Title,
    string Excerpt,
    string? Kind = null);

public sealed record VaultSemanticMatch(
    string Path,
    string Title,
    string? Heading,
    string TextPreview);

public sealed record VaultContextNote(
    string Path,
    string Title,
    string Content,
    bool IsTruncated = false,
    string? Kind = null,
    IReadOnlyList<string>? Aliases = null,
    VaultStructuredContent? Structured = null);

internal static class VaultToolPayloads
{
    public static VaultMatch FromSearchResult(VaultSearchResult result)
        => new(result.Path, result.Title, result.Excerpt, result.Kind);

    public static IReadOnlyList<VaultMatch> FromSearchResults(IEnumerable<VaultSearchResult> results)
        => results.Select(FromSearchResult).ToArray();

    public static VaultSemanticMatch FromSemanticHit(SemanticSearchHit hit)
        => new(hit.Path, hit.Title, hit.Heading, hit.TextPreview);

    public static IReadOnlyList<VaultSemanticMatch> FromSemanticHits(IEnumerable<SemanticSearchHit> hits)
        => hits.Select(FromSemanticHit).ToArray();

    public static VaultContextNote FromDocument(VaultNoteDocument note)
    {
        var aliases = note.Aliases is { Count: > 0 } ? note.Aliases : null;
        return new VaultContextNote(note.Path, note.Title, note.Content.Trim(), note.IsTruncated, note.Kind, aliases, note.Structured);
    }

    public static VaultContextNote FromContextDocument(VaultNoteDocument note, string query, int maxChars)
    {
        var aliases = note.Aliases is { Count: > 0 } ? note.Aliases : null;
        var normalizedContent = note.Content.Trim();
        var content = BuildContextSnippet(normalizedContent, query, maxChars);
        var isTruncated = note.IsTruncated || !string.Equals(content, normalizedContent, StringComparison.Ordinal);
        return new VaultContextNote(note.Path, note.Title, content, isTruncated, note.Kind, aliases, note.Structured);
    }

    public static IReadOnlyList<VaultContextNote> FromDocuments(IEnumerable<VaultNoteDocument> notes)
        => notes.Select(FromDocument).ToArray();

    public static IReadOnlyList<VaultContextNote> FromContextDocuments(IEnumerable<VaultNoteDocument> notes, string query, int maxChars)
        => notes.Select(note => FromContextDocument(note, query, maxChars)).ToArray();

    private static string BuildContextSnippet(string bodyContent, string query, int maxChars)
    {
        var normalizedBody = bodyContent.Trim();
        if (string.IsNullOrWhiteSpace(normalizedBody) || normalizedBody.Length <= maxChars)
            return normalizedBody;

        var sections = SplitSections(normalizedBody);
        var bestSection = sections
            .Select(section => new { Section = section, Score = ScoreSection(section, query) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Section.Index)
            .FirstOrDefault();

        if (bestSection is not null && bestSection.Score > 0)
            return TrimSection(bestSection.Section.Heading, bestSection.Section.Text, query, maxChars);

        return TrimSection(null, normalizedBody, query, maxChars);
    }

    private static string TrimSection(string? heading, string text, string query, int maxChars)
    {
        var prefix = string.IsNullOrWhiteSpace(heading) ? string.Empty : $"# {heading}\n\n";
        var budget = Math.Max(64, maxChars - prefix.Length);
        var anchor = FindBestAnchor(text, query);
        var snippet = anchor is null
            ? text[..Math.Min(text.Length, budget)].TrimEnd()
            : text.BuildExcerpt(anchor);

        if (snippet.Length > budget)
            snippet = snippet[..Math.Min(snippet.Length, budget)].TrimEnd();

        if (!string.IsNullOrWhiteSpace(prefix) && !snippet.StartsWith('#'))
            return prefix + snippet;

        return snippet;
    }

    private static string? FindBestAnchor(string text, string query)
    {
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return query;

        return query.ExtractTerms()
            .OrderByDescending(term => term.Length)
            .FirstOrDefault(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static int ScoreSection((int Index, string? Heading, string Text) section, string query)
    {
        var score = 0;
        var normalizedQuery = query.NormalizeForComparison();
        var queryTerms = query.ExtractTerms();

        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            var normalizedHeading = section.Heading.NormalizeForComparison();
            if (normalizedHeading.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                score += 8;
            else if (normalizedHeading.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                score += 5;
        }

        if (section.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 6;

        foreach (var term in queryTerms)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading) && section.Heading.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 3;

            if (section.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 2;
        }

        return score;
    }

    private static IReadOnlyList<(int Index, string? Heading, string Text)> SplitSections(string bodyContent)
    {
        var sections = new List<(int Index, string? Heading, string Text)>();
        var lines = bodyContent.Split('\n');
        var currentHeading = default(string?);
        var current = new List<string>();
        var index = 0;

        void Flush()
        {
            var text = string.Join("\n", current).Trim();
            current.Clear();
            if (!string.IsNullOrWhiteSpace(text))
                sections.Add((index++, currentHeading, text));
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var heading = trimmed.TrimStart('#', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(heading))
                {
                    Flush();
                    currentHeading = heading;
                    continue;
                }
            }

            current.Add(line);
        }

        Flush();
        return sections;
    }
}
