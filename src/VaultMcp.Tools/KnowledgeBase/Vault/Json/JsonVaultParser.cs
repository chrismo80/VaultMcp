using System.Text;
using System.Text.Json;
using VaultMcp.Tools.KnowledgeBase;

namespace VaultMcp.Tools.KnowledgeBase.Vault.Json;

internal static class JsonVaultParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static JsonParsedNote Parse(string rawContent, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(rawContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackTitle);

        var note = JsonSerializer.Deserialize<JsonVaultNote>(rawContent, SerializerOptions)
            ?? throw new JsonException("json note is empty or invalid.");

        var title = string.IsNullOrWhiteSpace(note.Title)
            ? fallbackTitle
            : note.Title.Trim();

        var tags = NormalizeList(note.Tags);
        var aliases = NormalizeList(note.Aliases);
        var related = NormalizeList(note.Related);
        var scalars = NormalizeScalars(note.Scalars);
        var lists = NormalizeLists(note.Lists);
        var sections = NormalizeSections(note.Sections);
        var learnings = NormalizeLearnings(note.Learnings);

        var structured = new VaultStructuredContent(
            scalars.Count == 0 ? null : scalars,
            lists.Count == 0 ? null : lists,
            sections.Count == 0 ? null : sections,
            learnings.Count == 0 ? null : learnings);

        var metadata = new VaultMetadata(
            string.IsNullOrWhiteSpace(note.Kind) ? null : note.Kind.Trim(),
            tags,
            aliases,
            related,
            string.IsNullOrWhiteSpace(note.Confidence) ? null : note.Confidence.Trim());

        var bodyContent = JsonVaultRenderer.Render(title, note.Summary, note.Details, structured);
        var headings = JsonVaultRenderer.BuildHeadings(note.Summary, note.Details, structured);
        var learningHashes = learnings.Select(learning => learning.Hash).ToArray();
        return new JsonParsedNote(
            rawContent,
            title,
            string.IsNullOrWhiteSpace(note.Summary) ? null : note.Summary.Trim(),
            string.IsNullOrWhiteSpace(note.Details) ? null : note.Details.Trim(),
            bodyContent,
            headings,
            metadata,
            structured,
            learningHashes);
    }

    internal static IReadOnlyDictionary<string, string> NormalizeScalars(IReadOnlyDictionary<string, string>? scalars)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (scalars is null)
            return normalized;

        foreach (var pair in scalars)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                continue;

            normalized[NormalizeKey(pair.Key)] = pair.Value.Trim();
        }

        return normalized;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizeLists(IReadOnlyDictionary<string, IReadOnlyList<string>>? lists)
    {
        var normalized = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (lists is null)
            return normalized;

        foreach (var pair in lists)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var values = NormalizeList(pair.Value);
            if (values.Count == 0)
                continue;

            normalized[NormalizeKey(pair.Key)] = values;
        }

        return normalized;
    }

    internal static IReadOnlyList<VaultSection> NormalizeSections(IReadOnlyList<JsonVaultSection>? sections)
    {
        if (sections is null || sections.Count == 0)
            return [];

        var normalized = new List<VaultSection>();
        foreach (var section in sections)
        {
            if (section is null)
                continue;

            var title = string.IsNullOrWhiteSpace(section.Title)
                ? NormalizeKey(section.Id ?? "section").ToDisplayTitle()
                : section.Title.Trim();
            var id = string.IsNullOrWhiteSpace(section.Id)
                ? title.ToSlug()
                : NormalizeKey(section.Id);
            var type = string.IsNullOrWhiteSpace(section.Type)
                ? (section.Items is { Count: > 0 } ? "list" : "text")
                : section.Type.Trim().ToLowerInvariant();
            var content = string.IsNullOrWhiteSpace(section.Content) ? null : section.Content.Trim();
            var items = NormalizeList(section.Items);

            if (string.IsNullOrWhiteSpace(content) && items.Count == 0)
                continue;

            normalized.Add(new VaultSection(id, title, type, content, items.Count == 0 ? null : items));
        }

        return normalized;
    }

    internal static IReadOnlyList<VaultStructuredLearning> NormalizeLearnings(IReadOnlyList<JsonVaultLearning>? learnings)
    {
        if (learnings is null || learnings.Count == 0)
            return [];

        var normalized = new List<VaultStructuredLearning>();
        foreach (var learning in learnings)
        {
            if (learning is null || string.IsNullOrWhiteSpace(learning.Hash) || string.IsNullOrWhiteSpace(learning.Summary))
                continue;

            var scalars = NormalizeScalars(learning.Scalars);
            var lists = NormalizeLists(learning.Lists);
            normalized.Add(new VaultStructuredLearning(
                learning.Hash.Trim(),
                learning.CapturedAt ?? DateTimeOffset.MinValue,
                learning.Summary.Trim(),
                string.IsNullOrWhiteSpace(learning.Details) ? null : learning.Details.Trim(),
                scalars.Count == 0 ? null : scalars,
                lists.Count == 0 ? null : lists));
        }

        return normalized;
    }

    internal static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
                continue;

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }

    internal static string ToDisplayTitle(this string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var parts = key.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    internal static string ToSlug(this string value) => NormalizeKey(value);

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal static class JsonVaultRenderer
{
    public static string Render(string title, string? summary, string? details, VaultStructuredContent structured)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {title.Trim()}");

        if (!string.IsNullOrWhiteSpace(summary))
        {
            builder.AppendLine();
            builder.AppendLine(summary.Trim());
        }

        AppendTextSection(builder, "Details", details);

        if (structured.Scalars is not null)
        {
            foreach (var pair in structured.Scalars.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                AppendTextSection(builder, pair.Key.ToDisplayTitle(), pair.Value);
        }

        if (structured.Lists is not null)
        {
            foreach (var pair in structured.Lists.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                AppendListSection(builder, pair.Key.ToDisplayTitle(), pair.Value);
        }

        if (structured.Sections is not null)
        {
            foreach (var section in structured.Sections)
                AppendSection(builder, section);
        }

        if (structured.Learnings is not null)
        {
            foreach (var learning in structured.Learnings.OrderBy(learning => learning.CapturedAt))
                AppendLearning(builder, learning);
        }

        return builder.ToString().TrimEnd();
    }

    public static IReadOnlyList<string> BuildHeadings(string? summary, string? details, VaultStructuredContent structured)
    {
        var headings = new List<string>();

        void Add(string? heading)
        {
            if (!string.IsNullOrWhiteSpace(heading))
                headings.Add(heading.Trim());
        }

        if (!string.IsNullOrWhiteSpace(summary))
            Add("Summary");
        if (!string.IsNullOrWhiteSpace(details))
            Add("Details");
        if (structured.Scalars is not null)
        {
            foreach (var key in structured.Scalars.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                Add(key.ToDisplayTitle());
        }

        if (structured.Lists is not null)
        {
            foreach (var key in structured.Lists.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                Add(key.ToDisplayTitle());
        }

        if (structured.Sections is not null)
        {
            foreach (var section in structured.Sections)
                Add(section.Title);
        }

        if (structured.Learnings is not null && structured.Learnings.Count > 0)
            Add("Learned");

        return headings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AppendSection(StringBuilder builder, VaultSection section)
    {
        if (!string.IsNullOrWhiteSpace(section.Content))
            AppendTextSection(builder, section.Title, section.Content);

        if (section.Items is { Count: > 0 })
            AppendListSection(builder, section.Title, section.Items);
    }

    private static void AppendLearning(StringBuilder builder, VaultStructuredLearning learning)
    {
        builder.AppendLine();
        builder.AppendLine($"## Learned {learning.CapturedAt:yyyy-MM-dd HH:mm 'UTC'}");
        builder.AppendLine();
        builder.AppendLine(learning.Summary.Trim());

        if (!string.IsNullOrWhiteSpace(learning.Details))
        {
            builder.AppendLine();
            builder.AppendLine(learning.Details.Trim());
        }

        if (learning.Scalars is not null)
        {
            foreach (var pair in learning.Scalars.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                AppendTextSection(builder, pair.Key.ToDisplayTitle(), pair.Value);
        }

        if (learning.Lists is not null)
        {
            foreach (var pair in learning.Lists.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                AppendListSection(builder, pair.Key.ToDisplayTitle(), pair.Value);
        }
    }

    private static void AppendTextSection(StringBuilder builder, string title, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine(content.Trim());
    }

    private static void AppendListSection(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return;

        builder.AppendLine();
        builder.AppendLine($"## {title}");
        foreach (var value in values)
            builder.AppendLine($"- {value}");
    }
}
