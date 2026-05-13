using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VaultMcp.Tools.KnowledgeBase.Vault.Json;

internal static class JsonVaultCaptureExtensions
{
    private static readonly string[] CaptureKinds = ["term", "workflow", "data-flow", "invariant", "pitfall", "decision"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string NormalizeKind(this string kind)
    {
        var normalized = kind.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        return normalized switch
        {
            "term" or "terms" or "glossary" or "domain-term" or "concept" or "concepts" => "term",
            "workflow" or "workflows" => "workflow",
            "data-flow" or "dataflow" or "flow" => "data-flow",
            "invariant" or "rule" or "rules" => "invariant",
            "pitfall" or "gotcha" => "pitfall",
            "decision" or "adr" => "decision",
            _ when CaptureKinds.Contains(normalized, StringComparer.OrdinalIgnoreCase) => normalized,
            _ => throw new ArgumentException($"Unsupported learning kind '{kind}'. Allowed kinds: {string.Join(", ", CaptureKinds)}.", nameof(kind))
        };
    }

    public static string[] NormalizeTags(this IReadOnlyList<string>? tags, string kind)
    {
        var normalized = (tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToSlug())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var required in new[] { "domain", kind })
        {
            if (!normalized.Contains(required, StringComparer.OrdinalIgnoreCase))
                normalized.Add(required);
        }

        return normalized.ToArray();
    }

    public static string MapKindToDirectory(this string kind) => kind switch
    {
        "term" => "glossary",
        "workflow" => "workflows",
        "data-flow" => "data-flows",
        "invariant" => "invariants",
        "pitfall" => "pitfalls",
        "decision" => "decisions",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported learning kind.")
    };

    public static string BuildNewNoteJson(this VaultLearningCapture learning, string title, string kind, IReadOnlyList<string> tags, DateTimeOffset timestamp)
    {
        var normalized = learning.ToStructuredPayload(kind);
        var hash = learning.ComputeLearningHash(kind, title);
        var note = new JsonVaultNote(
            Schema: "vault-note/v1",
            Id: $"{kind}.{title.ToSlug()}",
            Kind: kind,
            Title: title.Trim(),
            Summary: learning.Summary.Trim(),
            Details: string.IsNullOrWhiteSpace(learning.Details) ? null : learning.Details.Trim(),
            Tags: tags.ToArray(),
            Aliases: NormalizeList(kind == "term" ? learning.Aliases : []),
            Related: [],
            Confidence: null,
            Scalars: normalized.Scalars.Count == 0 ? null : normalized.Scalars,
            Lists: normalized.Lists.Count == 0 ? null : normalized.Lists,
            Sections: [],
            Learnings:
            [
                new JsonVaultLearning(
                    hash,
                    timestamp,
                    learning.Summary.Trim(),
                    string.IsNullOrWhiteSpace(learning.Details) ? null : learning.Details.Trim(),
                    normalized.Scalars.Count == 0 ? null : normalized.Scalars,
                    normalized.Lists.Count == 0 ? null : normalized.Lists)
            ],
            Meta: new JsonVaultMeta(timestamp, timestamp));

        return JsonSerializer.Serialize(note, SerializerOptions);
    }

    public static bool ContainsLearning(this JsonParsedNote existing, VaultLearningCapture learning, string kind, string title)
    {
        var hash = learning.ComputeLearningHash(kind, title);
        return existing.LearningHashes.Contains(hash, StringComparer.OrdinalIgnoreCase);
    }

    public static string AppendLearningJson(this string rawContent, VaultLearningCapture learning, string kind, string title, DateTimeOffset timestamp)
    {
        var note = JsonSerializer.Deserialize<JsonVaultNote>(rawContent, SerializerOptions)
            ?? throw new JsonException("json note is empty or invalid.");
        var normalized = learning.ToStructuredPayload(kind);
        var hash = learning.ComputeLearningHash(kind, title);
        var learnings = (note.Learnings ?? [])
            .Where(existing => existing is not null)
            .ToList();
        learnings.Add(new JsonVaultLearning(
            hash,
            timestamp,
            learning.Summary.Trim(),
            string.IsNullOrWhiteSpace(learning.Details) ? null : learning.Details.Trim(),
            normalized.Scalars.Count == 0 ? null : normalized.Scalars,
            normalized.Lists.Count == 0 ? null : normalized.Lists));

        var updated = note with
        {
            Summary = learning.Summary.Trim(),
            Details = string.IsNullOrWhiteSpace(learning.Details) ? note.Details : learning.Details.Trim(),
            Scalars = MergeScalars(note.Scalars, normalized.Scalars),
            Lists = MergeLists(note.Lists, normalized.Lists),
            Learnings = learnings,
            Meta = note.Meta is null
                ? new JsonVaultMeta(timestamp, timestamp)
                : note.Meta with { UpdatedAt = timestamp }
        };

        return JsonSerializer.Serialize(updated, SerializerOptions);
    }

    public static string ComputeLearningHash(this VaultLearningCapture learning, string kind, string title)
    {
        var normalized = learning.ToStructuredPayload(kind);
        var canonical = string.Join("\n", new[]
        {
            $"kind={NormalizeScalar(kind)}",
            $"title={NormalizeScalar(title)}",
            $"summary={NormalizeScalar(learning.Summary)}",
            $"details={NormalizeScalar(learning.Details)}",
            $"scalars={string.Join("|", normalized.Scalars.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={NormalizeScalar(pair.Value)}"))}",
            $"lists={string.Join("|", normalized.Lists.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}=[{string.Join(",", pair.Value.Select(NormalizeScalar).OrderBy(x => x, StringComparer.Ordinal))}]"))}"
        });

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..12];
    }

    private static (Dictionary<string, string> Scalars, Dictionary<string, IReadOnlyList<string>> Lists) ToStructuredPayload(this VaultLearningCapture learning, string kind)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        switch (kind)
        {
            case "term":
                AddList("examples", learning.Examples);
                break;
            case "workflow":
                AddList("steps", learning.Steps);
                break;
            case "data-flow":
                AddScalar("source", learning.Source);
                AddScalar("sink", learning.Sink);
                AddList("steps", learning.Steps);
                break;
            case "invariant":
                AddScalar("scope", learning.Scope);
                AddScalar("failure-mode", learning.FailureMode);
                break;
            case "pitfall":
                AddScalar("symptom", learning.Symptom);
                AddScalar("cause", learning.Cause);
                AddScalar("fix", learning.Fix);
                break;
            case "decision":
                AddScalar("context", learning.Context);
                AddScalar("choice", learning.Choice);
                AddScalar("consequence", learning.Consequence);
                break;
        }

        return (scalars, lists);

        void AddScalar(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                scalars[key] = value.Trim();
        }

        void AddList(string key, IReadOnlyList<string>? values)
        {
            var normalized = NormalizeList(values);
            if (normalized.Length > 0)
                lists[key] = normalized;
        }
    }

    private static IReadOnlyDictionary<string, string>? MergeScalars(IReadOnlyDictionary<string, string>? existing, IReadOnlyDictionary<string, string> additions)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (existing is not null)
        {
            foreach (var pair in existing)
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    merged[JsonVaultParser.NormalizeKey(pair.Key)] = pair.Value.Trim();
        }

        foreach (var pair in additions)
            merged[JsonVaultParser.NormalizeKey(pair.Key)] = pair.Value.Trim();

        return merged.Count == 0 ? null : merged;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? MergeLists(IReadOnlyDictionary<string, IReadOnlyList<string>>? existing, IReadOnlyDictionary<string, IReadOnlyList<string>> additions)
    {
        var merged = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (existing is not null)
        {
            foreach (var pair in existing)
            {
                var values = NormalizeList(pair.Value);
                if (values.Length > 0)
                    merged[JsonVaultParser.NormalizeKey(pair.Key)] = values;
            }
        }

        foreach (var pair in additions)
        {
            if (merged.TryGetValue(pair.Key, out var existingValues))
                merged[pair.Key] = existingValues.Concat(pair.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            else
                merged[pair.Key] = NormalizeList(pair.Value);
        }

        return merged.Count == 0 ? null : merged;
    }

    private static string[] NormalizeList(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeScalar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasWhitespace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (lastWasWhitespace)
                    continue;

                builder.Append(' ');
                lastWasWhitespace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            lastWasWhitespace = false;
        }

        return builder.ToString();
    }
}
