using System.Security.Cryptography;
using System.Text;
using VaultMcp.Tools.KnowledgeBase.Vault.Json;

namespace VaultMcp.Tools.KnowledgeBase.SemanticIndex;

internal static class VaultNoteChunker
{
    public static IReadOnlyList<NoteChunk> Chunk(string relativePath, JsonParsedNote note, DateTimeOffset modifiedAt, int maxChunkWords, int maxPreviewChars)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(note);
        if (maxChunkWords <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChunkWords), "maxChunkWords must be greater than zero.");
        if (maxPreviewChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPreviewChars), "maxPreviewChars must be greater than zero.");

        var sections = SplitSections(note.BodyContent);
        var chunks = new List<NoteChunk>();
        var sectionOrdinal = 0;

        foreach (var section in sections)
        {
            var parts = SplitLargeSection(section.Text, maxChunkWords);
            var partOrdinal = 0;

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var bodyText = CollapseWhitespace(part);
                if (string.IsNullOrWhiteSpace(bodyText))
                    continue;

                var chunkId = BuildChunkId(relativePath, section.Heading, sectionOrdinal, partOrdinal);
                var embeddingText = BuildEmbeddingText(relativePath, note.Title, section.Heading, note.Metadata.Tags, note.Metadata.Aliases, bodyText);
                chunks.Add(new NoteChunk(
                    chunkId,
                    relativePath,
                    note.Title,
                    section.Heading,
                    embeddingText,
                    BuildPreview(bodyText, maxPreviewChars),
                    note.Metadata.Tags.ToArray(),
                    note.Metadata.Aliases.ToArray(),
                    Sha256Hex(relativePath + "\n" + section.Heading + "\n" + bodyText),
                    modifiedAt));

                partOrdinal++;
            }

            sectionOrdinal++;
        }

        if (chunks.Count > 0)
            return chunks;

        var fallbackText = CollapseWhitespace(note.BodyContent);
        if (string.IsNullOrWhiteSpace(fallbackText))
            fallbackText = note.Title;

        return
        [
            new NoteChunk(
                BuildChunkId(relativePath, note.Title, 0, 0),
                relativePath,
                note.Title,
                null,
                BuildEmbeddingText(relativePath, note.Title, null, note.Metadata.Tags, note.Metadata.Aliases, fallbackText),
                BuildPreview(fallbackText, maxPreviewChars),
                note.Metadata.Tags.ToArray(),
                note.Metadata.Aliases.ToArray(),
                Sha256Hex(relativePath + "\n" + fallbackText),
                modifiedAt)
        ];
    }

    internal static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyList<(string? Heading, string Text)> SplitSections(string bodyContent)
    {
        var sections = new List<(string? Heading, string Text)>();
        var builder = new StringBuilder();
        string? currentHeading = null;

        foreach (var rawLine in bodyContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (TryParseHeading(line, out var heading))
            {
                Flush();
                currentHeading = heading;
                continue;
            }

            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(line);
        }

        Flush();
        return sections;

        void Flush()
        {
            var text = builder.ToString().Trim();
            builder.Clear();

            if (!string.IsNullOrWhiteSpace(text))
                sections.Add((currentHeading, text));
        }
    }

    private static IReadOnlyList<string> SplitLargeSection(string text, int maxChunkWords)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        var currentWords = 0;

        foreach (var paragraph in text.Split([Environment.NewLine + Environment.NewLine, "\n\n"], StringSplitOptions.None))
        {
            var trimmedParagraph = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(trimmedParagraph))
                continue;

            var paragraphWords = CountWords(trimmedParagraph);
            if (paragraphWords > maxChunkWords)
            {
                Flush();
                foreach (var overflow in SplitParagraphByWords(trimmedParagraph, maxChunkWords))
                    chunks.Add(overflow);
                continue;
            }

            if (currentWords > 0 && currentWords + paragraphWords > maxChunkWords)
                Flush();

            if (current.Length > 0)
                current.AppendLine().AppendLine();

            current.Append(trimmedParagraph);
            currentWords += paragraphWords;
        }

        Flush();
        return chunks;

        void Flush()
        {
            if (current.Length == 0)
                return;

            chunks.Add(current.ToString().Trim());
            current.Clear();
            currentWords = 0;
        }
    }

    private static IEnumerable<string> SplitParagraphByWords(string paragraph, int maxChunkWords)
    {
        var words = paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index += maxChunkWords)
            yield return string.Join(" ", words.Skip(index).Take(maxChunkWords));
    }

    private static int CountWords(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool TryParseHeading(string line, out string heading)
    {
        heading = string.Empty;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            return false;

        heading = trimmed.TrimStart('#', ' ').Trim();
        return !string.IsNullOrWhiteSpace(heading);
    }

    private static string BuildChunkId(string relativePath, string? heading, int sectionOrdinal, int partOrdinal)
        => $"{relativePath}#s{sectionOrdinal}-{ToSlug(heading ?? "note")}:{partOrdinal}";

    private static string BuildEmbeddingText(string relativePath, string title, string? heading, IReadOnlyList<string> tags, IReadOnlyList<string> aliases, string bodyText)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Title: {title}");
        builder.AppendLine($"Path: {relativePath}");

        if (tags.Count > 0)
            builder.AppendLine($"Tags: {string.Join(", ", tags)}");

        if (aliases.Count > 0)
            builder.AppendLine($"Aliases: {string.Join(", ", aliases)}");

        if (!string.IsNullOrWhiteSpace(heading))
            builder.AppendLine($"Heading: {heading}");

        builder.AppendLine();
        builder.Append(bodyText);
        return builder.ToString().Trim();
    }

    private static string BuildPreview(string text, int maxPreviewChars)
    {
        var collapsed = CollapseWhitespace(text);
        if (collapsed.Length <= maxPreviewChars)
            return collapsed;

        return collapsed[..Math.Max(0, maxPreviewChars - 1)].TrimEnd() + "…";
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasWhitespace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (lastWasWhitespace)
                    continue;

                builder.Append(' ');
                lastWasWhitespace = true;
                continue;
            }

            builder.Append(ch);
            lastWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static string ToSlug(string value)
        => JsonVaultParser.NormalizeKey(value);
}
