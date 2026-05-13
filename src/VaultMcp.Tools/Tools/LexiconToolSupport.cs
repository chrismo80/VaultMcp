using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Search.Lexical;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

internal sealed record LexiconEntry(
    string Path,
    string Term,
    string Summary,
    string Description,
    string Kind,
    string? Group,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Mentions,
    IReadOnlyList<string> SeeAlso,
    IReadOnlyList<string> NextQuestions);

internal static class LexiconToolSupport
{
    public static LexiconEntry? Explain(IVault vault, string term, int maxChars = 4000)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrWhiteSpace(term);

        var matches = vault.FindTerm(term, 5)
            .Concat(vault.SearchNotes(term, 5))
            .GroupBy(match => match.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(match => match.Score).First())
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
            return null;

        var note = vault.GetNote(matches[0].Path, maxChars);
        var description = BuildDescription(note);
        var summary = BuildSummary(note, description);
        var allNotes = LoadNotes(vault);
        var group = TryGetGroup(note);
        var mentions = FindMentions(note.Title, note.Aliases, description, allNotes);
        var sameGroup = FindSameGroupTerms(note.Title, group, allNotes);
        var seeAlso = mentions
            .Concat(sameGroup)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        var nextQuestions = BuildNextQuestions(note.Title, seeAlso);

        return new LexiconEntry(
            note.Path,
            note.Title,
            summary,
            description,
            note.Kind ?? "term",
            group,
            note.Aliases ?? [],
            mentions,
            seeAlso,
            nextQuestions);
    }

    public static IReadOnlyList<VaultNoteDocument> LoadNotes(IVault vault, int maxChars = 2000)
    {
        var status = vault.GetStatus();
        var count = Math.Max(1, status.NoteCount);
        return vault.ListNotes(count)
            .Select(note => vault.GetNote(note.Path, maxChars))
            .ToArray();
    }

    public static string BuildDescription(VaultNoteDocument note)
    {
        if (!string.IsNullOrWhiteSpace(note.Details))
            return string.IsNullOrWhiteSpace(note.Summary)
                ? note.Details.Trim()
                : $"{note.Summary.Trim()}\n\n{note.Details.Trim()}";

        if (!string.IsNullOrWhiteSpace(note.Summary))
            return note.Summary.Trim();

        return note.Content.Trim();
    }

    public static string BuildSummary(VaultNoteDocument note, string? fallbackDescription = null)
    {
        if (!string.IsNullOrWhiteSpace(note.Summary))
            return note.Summary.Trim();

        var source = string.IsNullOrWhiteSpace(fallbackDescription) ? note.Content : fallbackDescription;
        var normalized = source.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return note.Title;

        var line = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .FirstOrDefault(part => !part.StartsWith('#'));

        return string.IsNullOrWhiteSpace(line) ? note.Title : line;
    }

    public static string? TryGetGroup(VaultNoteDocument note)
    {
        if (note.Structured?.Scalars is not null && note.Structured.Scalars.TryGetValue("group", out var group) && !string.IsNullOrWhiteSpace(group))
            return group.Trim();

        return null;
    }

    public static LexiconTermSummary ToTermSummary(VaultNoteDocument note)
        => new(
            note.Title,
            BuildSummary(note),
            TryGetGroup(note),
            note.Aliases,
            note.Path,
            note.Kind ?? "term");

    private static IReadOnlyList<string> FindMentions(string title, IReadOnlyList<string>? aliases, string description, IReadOnlyList<VaultNoteDocument> allNotes)
    {
        var selfNames = new[] { title }
            .Concat(aliases ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return allNotes
            .Where(candidate => !selfNames.Any(name => name.Equals(candidate.Title, StringComparison.OrdinalIgnoreCase)))
            .Where(candidate => description.Contains(candidate.Title, StringComparison.OrdinalIgnoreCase)
                || (candidate.Aliases ?? []).Any(alias => description.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            .Select(candidate => candidate.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static IReadOnlyList<string> FindSameGroupTerms(string title, string? group, IReadOnlyList<VaultNoteDocument> allNotes)
    {
        if (string.IsNullOrWhiteSpace(group))
            return [];

        return allNotes
            .Where(candidate => !candidate.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => string.Equals(TryGetGroup(candidate), group, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildNextQuestions(string term, IReadOnlyList<string> seeAlso)
    {
        var questions = new List<string>();
        if (seeAlso.Count > 0)
            questions.Add($"Wie unterscheidet sich {term} von {seeAlso[0]}?");
        if (seeAlso.Count > 1)
            questions.Add($"Wie hängt {term} mit {seeAlso[1]} zusammen?");
        questions.Add($"Wo taucht {term} im System konkret auf?");
        return questions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed record LexiconTermSummary(
    string Term,
    string OneLine,
    string? Group = null,
    IReadOnlyList<string>? Aliases = null,
    string? Path = null,
    string? Kind = null);
