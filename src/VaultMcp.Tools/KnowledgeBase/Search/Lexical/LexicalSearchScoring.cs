namespace VaultMcp.Tools.KnowledgeBase.Search.Lexical;

internal static class LexicalSearchScoring
{
    private static readonly LexicalSearchScoringOptions Scoring = LexicalSearchScoringOptions.Default;

    public static int ScoreSearchResult(VaultIndexedNote note, string query)
    {
        var normalizedQuery = NormalizeQuery(query);
        var queryTerms = normalizedQuery.ExtractTerms().ToArray();
        var options = Scoring.SearchNotes;

        var pathScore = ScorePath(note.RelativePath, normalizedQuery, queryTerms, Scoring.Path);
        var titleScore = ScoreText(note.Title, normalizedQuery, queryTerms, options.Title);
        var headingScore = ScoreList(note.Headings, normalizedQuery, queryTerms, options.Headings);
        var aliasScore = ScoreList(note.Metadata.Aliases, normalizedQuery, queryTerms, options.Aliases);
        var tagScore = ScoreList(note.Metadata.Tags, normalizedQuery, queryTerms, options.Tags);
        var kindScore = ScoreSingle(note.Metadata.Kind, normalizedQuery, options.Kind);
        var contentScore = ScoreContent(note.BodyContent, normalizedQuery, queryTerms, options.Content);
        return pathScore + titleScore + headingScore + aliasScore + tagScore + kindScore + contentScore;
    }

    public static int ScoreTermResult(VaultIndexedNote note, string query)
    {
        var normalizedQuery = NormalizeQuery(query);
        var queryTerms = normalizedQuery.ExtractTerms().ToArray();
        var fileName = Path.GetFileNameWithoutExtension(note.RelativePath);
        var options = Scoring.FindTerm;
        var score = 0;

        score += ScoreText(note.Title, normalizedQuery, queryTerms, options.Title);
        score += ScoreFileName(fileName, normalizedQuery, options.FileName);
        score += ScorePath(note.RelativePath, normalizedQuery, queryTerms, Scoring.Path);
        score += ScoreList(note.Metadata.Aliases, normalizedQuery, queryTerms, options.Aliases);
        score += ScoreList(note.Headings, normalizedQuery, queryTerms, options.Headings);
        score += ScoreList(note.Metadata.Tags, normalizedQuery, queryTerms, options.Tags);
        score += ScoreSingle(note.Metadata.Kind, normalizedQuery, options.Kind);
        if (string.Equals(note.Metadata.Kind, "term", StringComparison.OrdinalIgnoreCase))
        {
            score += options.TermKindBoost;

            if (note.Metadata.Aliases.Any(alias => alias.NormalizeForComparison().Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
                score += options.ExactAliasOnTermBoost;
        }

        score += ScoreContent(note.BodyContent, normalizedQuery, queryTerms, options.Content);
        return score;
    }

    public static int ScoreRelatedNote(VaultIndexedNote source, VaultIndexedNote candidate)
    {
        var options = Scoring.RelatedNotes;
        var score = 0;

        if (source.Metadata.Related.Contains(candidate.RelativePath, StringComparer.OrdinalIgnoreCase))
            score += options.ExplicitRelatedBoost;
        if (candidate.Metadata.Related.Contains(source.RelativePath, StringComparer.OrdinalIgnoreCase))
            score += options.ExplicitRelatedBoost;

        var sharedTerms = source.ExtractedTerms.Intersect(candidate.ExtractedTerms, StringComparer.OrdinalIgnoreCase)
            .Take(options.MaxSharedTerms)
            .ToArray();
        score += sharedTerms.Length * options.SharedTermBoost;

        foreach (var term in sharedTerms)
        {
            if (candidate.RelativePath.NormalizeForComparison().Contains(term, StringComparison.OrdinalIgnoreCase))
                score += options.SharedTermInPathBoost;
        }

        var sourceDirectory = Path.GetDirectoryName(source.RelativePath) ?? string.Empty;
        var candidateDirectory = Path.GetDirectoryName(candidate.RelativePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(sourceDirectory) &&
            string.Equals(sourceDirectory, candidateDirectory, StringComparison.OrdinalIgnoreCase))
        {
            score += options.SameDirectoryBoost;
        }

        var sharedTags = source.Metadata.Tags.Intersect(candidate.Metadata.Tags, StringComparer.OrdinalIgnoreCase).Count();
        score += sharedTags * options.SharedTagBoost;
        score += ScoreKindAffinity(source.Metadata.Kind, candidate.Metadata.Kind);

        return score;
    }

    private static int ScorePath(string relativePath, string query, IReadOnlyList<string> queryTerms, TextMatchScoringOptions options)
    {
        var comparablePath = relativePath.NormalizeForComparison();
        if (comparablePath.Equals(query, StringComparison.OrdinalIgnoreCase))
            return options.ExactMatchBoost;
        if (comparablePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            return options.ContainsBoost + ScoreExactPhraseBoost(comparablePath, query, options.ExactPhraseBoost);

        return ScoreTermCoverage(comparablePath, queryTerms, options.PerTermBoost);
    }

    private static int ScoreText(string text, string query, IReadOnlyList<string> queryTerms, TextMatchScoringOptions options)
    {
        var comparableText = text.NormalizeForComparison();
        var score = 0;

        if (comparableText.Equals(query, StringComparison.OrdinalIgnoreCase))
            score += options.ExactMatchBoost;
        else if (options.StartsWithBoost > 0 && comparableText.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            score += options.StartsWithBoost;
        else if (options.ContainsBoost > 0 && comparableText.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += options.ContainsBoost;

        score += ScoreExactPhraseBoost(comparableText, query, options.ExactPhraseBoost);
        score += ScoreTermCoverage(comparableText, queryTerms, options.PerTermBoost);
        return score;
    }

    private static int ScoreFileName(string fileName, string query, TextMatchScoringOptions options)
    {
        var comparableFileName = fileName.NormalizeForComparison();
        if (comparableFileName.Equals(query, StringComparison.OrdinalIgnoreCase))
            return options.ExactMatchBoost;
        if (comparableFileName.Contains(query, StringComparison.OrdinalIgnoreCase))
            return options.ContainsBoost;

        return 0;
    }

    private static int ScoreSingle(string? value, string query, SingleValueMatchScoringOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var comparableValue = value.NormalizeForComparison();
        if (comparableValue.Equals(query, StringComparison.OrdinalIgnoreCase))
            return options.ExactMatchBoost;
        if (comparableValue.Contains(query, StringComparison.OrdinalIgnoreCase))
            return options.ContainsBoost + ScoreExactPhraseBoost(comparableValue, query, options.ExactPhraseBoost);

        return 0;
    }

    private static int ScoreList(IReadOnlyList<string> values, string query, IReadOnlyList<string> queryTerms, ListMatchScoringOptions options)
    {
        var score = 0;
        foreach (var value in values)
        {
            var comparableValue = value.NormalizeForComparison();
            if (comparableValue.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                score += options.ExactMatchBoost;
                continue;
            }

            if (comparableValue.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += options.ContainsBoost + ScoreExactPhraseBoost(comparableValue, query, options.ExactPhraseBoost);

            score += ScoreTermCoverage(comparableValue, queryTerms, options.PerTermBoost);
        }

        return score;
    }

    private static int ScoreContent(string content, string query, IReadOnlyList<string> queryTerms, ContentMatchScoringOptions options)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var comparableContent = content.NormalizeForComparison();
        var score = 0;
        if (comparableContent.Equals(query, StringComparison.OrdinalIgnoreCase))
            score += options.ExactMatchBoost;
        else if (comparableContent.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += options.ContainsBoost + ScoreExactPhraseBoost(comparableContent, query, options.ExactPhraseBoost);

        score += CountOccurrences(comparableContent, query) * options.PerOccurrenceBoost;
        score += ScoreTermCoverage(comparableContent, queryTerms, options.PerTermBoost);
        return score;
    }

    private static int ScoreTermCoverage(string text, IReadOnlyList<string> queryTerms, int perTermBoost)
    {
        if (string.IsNullOrWhiteSpace(text) || queryTerms.Count == 0)
            return 0;

        var comparableText = text.NormalizeForComparison();
        var score = 0;
        foreach (var term in queryTerms)
        {
            if (comparableText.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += perTermBoost;
        }

        return score;
    }

    private static int ScoreExactPhraseBoost(string text, string query, int boost)
    {
        if (boost <= 0 || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
            return 0;

        var comparableText = text.NormalizeForComparison();
        var normalizedQuery = NormalizeQuery(query);
        if (!normalizedQuery.Contains(' ', StringComparison.Ordinal))
            return 0;

        return comparableText.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ? boost : 0;
    }

    private static int ScoreKindAffinity(string? sourceKind, string? candidateKind)
    {
        var options = Scoring.KindAffinity;

        if (string.IsNullOrWhiteSpace(sourceKind) || string.IsNullOrWhiteSpace(candidateKind))
            return 0;

        if (string.Equals(sourceKind, candidateKind, StringComparison.OrdinalIgnoreCase))
            return options.SameKindBoost;

        return (sourceKind.ToLowerInvariant(), candidateKind.ToLowerInvariant()) switch
        {
            ("workflow", "invariant") => options.WorkflowToInvariantBoost,
            ("workflow", "decision") => options.WorkflowToDecisionBoost,
            ("workflow", "data-flow") => options.WorkflowToDataFlowBoost,
            ("invariant", "workflow") => options.InvariantToWorkflowBoost,
            ("invariant", "decision") => options.InvariantToDecisionBoost,
            ("decision", "workflow") => options.DecisionToWorkflowBoost,
            ("decision", "invariant") => options.DecisionToInvariantBoost,
            ("data-flow", "workflow") => options.DataFlowToWorkflowBoost,
            ("data-flow", "invariant") => options.DataFlowToInvariantBoost,
            _ => 0
        };
    }

    private static string NormalizeQuery(string query) => query.NormalizeForComparison();

    private static int CountOccurrences(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
            return 0;

        var count = 0;
        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var index = text.IndexOf(query, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            count++;
            startIndex = index + query.Length;
        }

        return count;
    }
}
