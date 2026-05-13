namespace VaultMcp.Tools.KnowledgeBase.Search.Lexical;

internal sealed class LexicalSearch : ISearch
{
    public IReadOnlyList<VaultSearchResult> SearchNotes(IReadOnlyList<VaultIndexedNote> notes, string query, int maxCount = 10)
        => Search(notes, query, maxCount, LexicalSearchScoring.ScoreSearchResult);

    public IReadOnlyList<VaultSearchResult> FindTerm(IReadOnlyList<VaultIndexedNote> notes, string term, int maxCount = 10)
        => Search(notes, term, maxCount, LexicalSearchScoring.ScoreTermResult);

    public IReadOnlyList<VaultSearchResult> FindRelatedNotes(IReadOnlyList<VaultIndexedNote> notes, VaultIndexedNote source, int maxCount = 5)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(source);
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");

        return notes
            .Where(candidate => !string.Equals(candidate.RelativePath, source.RelativePath, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = LexicalSearchScoring.ScoreRelatedNote(source, candidate)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(x => new VaultSearchResult(
                x.Candidate.RelativePath,
                x.Candidate.Title,
                x.Candidate.BodyContent.BuildExcerpt(source.FindBestSharedTerm(x.Candidate) ?? x.Candidate.Title),
                x.Score,
                x.Candidate.Metadata.Kind,
                x.Candidate.Metadata.Tags))
            .ToArray();
    }

    private static IReadOnlyList<VaultSearchResult> Search(
        IReadOnlyList<VaultIndexedNote> notes,
        string query,
        int maxCount,
        Func<VaultIndexedNote, string, int> score)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");

        return notes
            .Select(note => new { Note = note, Score = score(note, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Note.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Note.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(x => new VaultSearchResult(
                x.Note.RelativePath,
                x.Note.Title,
                x.Note.BodyContent.BuildExcerpt(query),
                x.Score,
                x.Note.Metadata.Kind,
                x.Note.Metadata.Tags))
            .ToArray();
    }
}
