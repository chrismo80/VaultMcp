using System.Text;
using System.Text.Json;
using VaultMcp.Tools.KnowledgeBase.Search;
using VaultMcp.Tools.KnowledgeBase.Search.Lexical;

namespace VaultMcp.Tools.KnowledgeBase.Vault.Json;

public sealed class JsonVault : IVault, IDisposable
{
    private const int DefaultGetNoteMaxChars = 12000;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] Extensions = [".json"];
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly ISearch _search;
    private readonly object _sync = new();
    private readonly string _rootPath;
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private Dictionary<string, VaultIndexedNote> _index = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private FileSystemWatcher? _watcher;
    private bool _indexDirty = true;

    public JsonVault(string rootPath)
        : this(rootPath, new LexicalSearch())
    {
    }

    internal JsonVault(string rootPath, ISearch search)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(search);

        _rootPath = Path.GetFullPath(rootPath);
        _search = search;

        EnsureWatcher();
    }

    public VaultStatus GetStatus()
    {
        var notes = GetIndexedNotes();
        return new VaultStatus(_rootPath, Directory.Exists(_rootPath), notes.Count, Extensions);
    }

    public IReadOnlyList<VaultNote> ListNotes(int maxCount = 100)
    {
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");

        return GetIndexedNotes()
            .OrderBy(note => note.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(note => new VaultNote(note.RelativePath, note.Title))
            .ToArray();
    }

    public VaultNoteDocument GetNote(string relativePath, int maxChars = DefaultGetNoteMaxChars)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (maxChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "maxChars must be greater than zero.");

        var fullPath = ResolvePath(relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Note '{relativePath}' was not found in the vault.", relativePath);

        var entry = GetIndexedNote(fullPath);
        var content = entry.BodyContent;
        var truncated = false;
        if (content.Length > maxChars)
        {
            content = content[..maxChars];
            truncated = true;
        }

        return new VaultNoteDocument(
            entry.RelativePath,
            entry.Title,
            content,
            truncated,
            entry.Metadata.Kind,
            entry.Metadata.Tags,
            entry.Metadata.Aliases,
            entry.Headings,
            ReadStructured(fullPath),
            ReadSummary(fullPath),
            ReadDetails(fullPath));
    }

    public IReadOnlyList<VaultSearchResult> SearchNotes(string query, int maxCount = 10)
        => _search.SearchNotes(GetIndexedNotes(), query, maxCount);

    public IReadOnlyList<VaultSearchResult> FindTerm(string term, int maxCount = 10)
        => _search.FindTerm(GetIndexedNotes(), term, maxCount);

    public IReadOnlyList<VaultSearchResult> FindRelatedNotes(string relativePath, int maxCount = 5)
        => _search.FindRelatedNotes(GetIndexedNotes(), GetIndexedNote(ResolvePath(relativePath)), maxCount);

    public VaultCaptureResult CaptureLearning(VaultLearningCapture learning)
    {
        ArgumentNullException.ThrowIfNull(learning);
        ArgumentException.ThrowIfNullOrWhiteSpace(learning.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(learning.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(learning.Summary);

        var normalizedKind = learning.Kind.NormalizeKind();
        var title = learning.Title.Trim();
        var tags = learning.Tags.NormalizeTags(normalizedKind);
        var relativePath = Path.Combine(normalizedKind.MapKindToDirectory(), title.ToSlug() + ".json");
        var fullPath = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (!File.Exists(fullPath))
        {
            File.WriteAllText(fullPath, learning.BuildNewNoteJson(title, normalizedKind, tags, DateTimeOffset.UtcNow), Utf8);
            RefreshIndex(fullPath);
            return new VaultCaptureResult(ToRelativePath(fullPath), title, normalizedKind, Created: true, Appended: false, Unchanged: false, Message: "Created new knowledge note.");
        }

        var existing = File.ReadAllText(fullPath, Utf8);
        var parsed = JsonVaultParser.Parse(existing, Path.GetFileNameWithoutExtension(fullPath));
        if (parsed.ContainsLearning(learning, normalizedKind, title))
            return new VaultCaptureResult(ToRelativePath(fullPath), title, normalizedKind, Created: false, Appended: false, Unchanged: true, Message: "Learning already present in note.");

        var updated = existing.AppendLearningJson(learning, normalizedKind, title, DateTimeOffset.UtcNow);
        File.WriteAllText(fullPath, updated, Utf8);
        RefreshIndex(fullPath);
        return new VaultCaptureResult(ToRelativePath(fullPath), title, normalizedKind, Created: false, Appended: true, Unchanged: false, Message: "Appended learning to existing note.");
    }

    public VaultTermCaptureResult CaptureTerm(VaultTermCapture term)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentException.ThrowIfNullOrWhiteSpace(term.Term);
        ArgumentException.ThrowIfNullOrWhiteSpace(term.Description);

        var timestamp = DateTimeOffset.UtcNow;
        var existing = FindExistingTermNote(term.Term, term.Aliases);
        if (existing is null)
        {
            var title = term.Term.Trim();
            var fullPath = ResolvePath(Path.Combine("glossary", title.ToSlug() + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            var createdNote = BuildLexiconNote(title, term.Description, term.Aliases, term.Group, timestamp);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(createdNote, JsonSerializerOptions), Utf8);
            RefreshIndex(fullPath);

            return new VaultTermCaptureResult(
                ToRelativePath(fullPath),
                title,
                Created: true,
                Updated: false,
                Unchanged: false,
                Message: "Created new lexicon term.",
                createdNote.Aliases,
                createdNote.Scalars is not null && createdNote.Scalars.TryGetValue("group", out var createdGroup) ? createdGroup : null);
        }

        var rawContent = File.ReadAllText(existing.FullPath, Utf8);
        var note = JsonSerializer.Deserialize<JsonVaultNote>(rawContent, JsonSerializerOptions)
            ?? throw new JsonException("json note is empty or invalid.");

        var updated = MergeLexiconNote(note, term, timestamp, out var changed);
        if (!changed)
        {
            return new VaultTermCaptureResult(
                existing.RelativePath,
                string.IsNullOrWhiteSpace(note.Title) ? term.Term.Trim() : note.Title.Trim(),
                Created: false,
                Updated: false,
                Unchanged: true,
                Message: "Lexicon term already up to date.",
                note.Aliases,
                note.Scalars is not null && note.Scalars.TryGetValue("group", out var existingGroup) ? existingGroup : null);
        }

        File.WriteAllText(existing.FullPath, JsonSerializer.Serialize(updated, JsonSerializerOptions), Utf8);
        RefreshIndex(existing.FullPath);

        return new VaultTermCaptureResult(
            existing.RelativePath,
            updated.Title ?? term.Term.Trim(),
            Created: false,
            Updated: true,
            Unchanged: false,
            Message: "Updated existing lexicon term.",
            updated.Aliases,
            updated.Scalars is not null && updated.Scalars.TryGetValue("group", out var mergedGroup) ? mergedGroup : null);
    }

    private VaultStructuredContent ReadStructured(string fullPath)
    {
        var raw = File.ReadAllText(fullPath, Utf8);
        return JsonVaultParser.Parse(raw, Path.GetFileNameWithoutExtension(fullPath)).Structured;
    }

    private string? ReadSummary(string fullPath)
    {
        var raw = File.ReadAllText(fullPath, Utf8);
        return JsonVaultParser.Parse(raw, Path.GetFileNameWithoutExtension(fullPath)).Summary;
    }

    private string? ReadDetails(string fullPath)
    {
        var raw = File.ReadAllText(fullPath, Utf8);
        return JsonVaultParser.Parse(raw, Path.GetFileNameWithoutExtension(fullPath)).Details;
    }

    private IReadOnlyList<VaultIndexedNote> GetIndexedNotes()
    {
        Dictionary<string, VaultIndexedNote> currentIndex;

        lock (_sync)
        {
            EnsureWatcher();

            if (!_indexDirty)
                return _index.Values.ToArray();

            currentIndex = new Dictionary<string, VaultIndexedNote>(_index, _pathComparer);
        }

        var nextIndex = BuildIndexSnapshot(currentIndex);

        lock (_sync)
        {
            EnsureWatcher();

            if (_indexDirty)
            {
                _index = nextIndex;
                _indexDirty = false;
            }

            return _index.Values.ToArray();
        }
    }

    private Dictionary<string, VaultIndexedNote> BuildIndexSnapshot(IReadOnlyDictionary<string, VaultIndexedNote> currentIndex)
    {
        var currentFiles = EnumerateFiles().ToArray();
        var nextIndex = new Dictionary<string, VaultIndexedNote>(_pathComparer);

        foreach (var path in currentFiles)
        {
            var info = new FileInfo(path);
            if (currentIndex.TryGetValue(path, out var existing) &&
                existing.LastWriteTimeUtc == info.LastWriteTimeUtc &&
                existing.FileSizeBytes == info.Length)
            {
                nextIndex[path] = existing;
                continue;
            }

            nextIndex[path] = LoadIndexedNote(path, info);
        }

        return nextIndex;
    }

    private VaultIndexedNote GetIndexedNote(string fullPath)
    {
        _ = GetIndexedNotes();
        if (_index.TryGetValue(fullPath, out var note))
            return note;

        throw new FileNotFoundException($"Note '{ToRelativePath(fullPath)}' was not found in the vault.", ToRelativePath(fullPath));
    }

    private VaultIndexedNote LoadIndexedNote(string path, FileInfo info)
    {
        var rawContent = File.ReadAllText(path, Utf8).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var truncated = false;
        var parsed = JsonVaultParser.Parse(rawContent, Path.GetFileNameWithoutExtension(path));
        var terms = string.Join(
                Environment.NewLine,
                new[]
                {
                    parsed.Title,
                    parsed.BodyContent,
                    string.Join(Environment.NewLine, parsed.Headings),
                    string.Join(Environment.NewLine, parsed.Metadata.Aliases),
                    string.Join(Environment.NewLine, parsed.Metadata.Tags)
                })
            .ExtractTerms();

        return new VaultIndexedNote(
            path,
            ToRelativePath(path),
            parsed.Title,
            parsed.RawContent,
            parsed.BodyContent,
            parsed.Headings,
            parsed.Metadata,
            terms,
            info.Length,
            info.LastWriteTimeUtc,
            truncated);
    }

    private IEnumerable<string> EnumerateFiles()
    {
        if (!Directory.Exists(_rootPath))
            return [];

        return Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.Combine(_rootPath, ".vault"), StringComparison.OrdinalIgnoreCase))
            .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    private string ResolvePath(string relativePath)
        => VaultPathGuard.ResolvePath(_rootPath, relativePath, Extensions);

    private void RefreshIndex(string fullPath)
    {
        lock (_sync)
        {
            EnsureWatcher();

            if (!File.Exists(fullPath))
            {
                _index.Remove(fullPath);
                _indexDirty = true;
                return;
            }

            var info = new FileInfo(fullPath);
            _index[fullPath] = LoadIndexedNote(fullPath, info);
            _indexDirty = true;
        }
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null || !Directory.Exists(_rootPath))
            return;

        _watcher = new FileSystemWatcher(_rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Changed += OnFileSystemChanged;
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemRenamed;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs args)
    {
        if (ShouldInvalidateForPath(args.FullPath))
            MarkIndexDirty();
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs args)
    {
        if (ShouldInvalidateForPath(args.FullPath) || ShouldInvalidateForPath(args.OldFullPath))
            MarkIndexDirty();
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        lock (_sync)
        {
            if (_watcher is not null)
            {
                _watcher.Dispose();
                _watcher = null;
            }

            _indexDirty = true;
        }
    }

    private void MarkIndexDirty()
    {
        lock (_sync)
        {
            _indexDirty = true;
        }
    }

    private static bool ShouldInvalidateForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension) || Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_watcher is null)
                return;

            _watcher.Dispose();
            _watcher = null;
        }
    }

    private string ToRelativePath(string path) => Path.GetRelativePath(_rootPath, path);

    private VaultIndexedNote? FindExistingTermNote(string term, IReadOnlyList<string>? aliases)
    {
        var candidates = GetIndexedNotes()
            .Where(note => string.Equals(note.Metadata.Kind, "term", StringComparison.OrdinalIgnoreCase)
                || note.RelativePath.StartsWith("glossary/", StringComparison.OrdinalIgnoreCase)
                || note.RelativePath.StartsWith("glossary\\", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var probes = new[] { term }
            .Concat(aliases ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.NormalizeForComparison())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var probe in probes)
        {
            var titleMatch = candidates.FirstOrDefault(note =>
                note.Title.NormalizeForComparison().Equals(probe, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(note.RelativePath).NormalizeForComparison().Equals(probe, StringComparison.OrdinalIgnoreCase));
            if (titleMatch is not null)
                return titleMatch;

            var aliasMatch = candidates.FirstOrDefault(note => note.Metadata.Aliases.Any(alias => alias.NormalizeForComparison().Equals(probe, StringComparison.OrdinalIgnoreCase)));
            if (aliasMatch is not null)
                return aliasMatch;
        }

        return null;
    }

    private static JsonVaultNote BuildLexiconNote(string title, string description, IReadOnlyList<string>? aliases, string? group, DateTimeOffset timestamp)
    {
        var normalizedAliases = NormalizeAliases(aliases, title);
        var (summary, details) = SplitDescription(description);
        var scalars = string.IsNullOrWhiteSpace(group)
            ? null
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["group"] = group.Trim() };

        return new JsonVaultNote(
            Schema: "vault-note/v1",
            Id: $"term.{title.ToSlug()}",
            Kind: "term",
            Title: title.Trim(),
            Summary: summary,
            Details: details,
            Tags: ["domain", "term"],
            Aliases: normalizedAliases.Length == 0 ? null : normalizedAliases,
            Related: null,
            Confidence: null,
            Scalars: scalars,
            Lists: null,
            Sections: null,
            Learnings: null,
            Meta: new JsonVaultMeta(timestamp, timestamp));
    }

    private static JsonVaultNote MergeLexiconNote(JsonVaultNote note, VaultTermCapture capture, DateTimeOffset timestamp, out bool changed)
    {
        var title = string.IsNullOrWhiteSpace(note.Title) ? capture.Term.Trim() : note.Title.Trim();
        var existingDescription = CombineDescription(note.Summary, note.Details);
        var mergedDescription = MergeDescription(existingDescription, capture.Description);
        var (summary, details) = SplitDescription(mergedDescription);

        var existingAliases = NormalizeAliases(note.Aliases, title);
        var incomingAliases = NormalizeAliases(capture.Aliases, title);
        var mergedAliases = existingAliases
            .Concat(incomingAliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedScalars = note.Scalars is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(note.Scalars, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(capture.Group) && !mergedScalars.ContainsKey("group"))
            mergedScalars["group"] = capture.Group.Trim();

        var normalizedExistingDescription = existingDescription.NormalizeForComparison();
        var normalizedMergedDescription = mergedDescription.NormalizeForComparison();
        var existingGroup = note.Scalars is not null && note.Scalars.TryGetValue("group", out var group) ? group : null;

        changed = !string.Equals(normalizedExistingDescription, normalizedMergedDescription, StringComparison.Ordinal)
            || !existingAliases.SequenceEqual(mergedAliases, StringComparer.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(capture.Group) && string.IsNullOrWhiteSpace(existingGroup));

        if (!changed)
            return note;

        return note with
        {
            Schema = string.IsNullOrWhiteSpace(note.Schema) ? "vault-note/v1" : note.Schema,
            Id = string.IsNullOrWhiteSpace(note.Id) ? $"term.{title.ToSlug()}" : note.Id,
            Kind = string.IsNullOrWhiteSpace(note.Kind) ? "term" : note.Kind,
            Title = title,
            Summary = summary,
            Details = details,
            Tags = MergeTags(note.Tags, "domain", "term"),
            Aliases = mergedAliases.Length == 0 ? null : mergedAliases,
            Scalars = mergedScalars.Count == 0 ? null : mergedScalars,
            Meta = note.Meta is null
                ? new JsonVaultMeta(timestamp, timestamp)
                : note.Meta with { UpdatedAt = timestamp }
        };
    }

    private static string[] MergeTags(IReadOnlyList<string>? existing, params string[] additions)
        => (existing ?? [])
            .Concat(additions)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] NormalizeAliases(IReadOnlyList<string>? aliases, string title)
        => (aliases ?? [])
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Where(alias => !alias.Equals(title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string CombineDescription(string? summary, string? details)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return details?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(details))
            return summary.Trim();
        return $"{summary.Trim()}\n\n{details.Trim()}";
    }

    private static string MergeDescription(string existing, string incoming)
    {
        var normalizedExisting = existing.NormalizeForComparison();
        var normalizedIncoming = incoming.NormalizeForComparison();

        if (string.IsNullOrWhiteSpace(existing))
            return incoming.Trim();
        if (string.IsNullOrWhiteSpace(incoming))
            return existing.Trim();
        if (normalizedExisting.Equals(normalizedIncoming, StringComparison.Ordinal))
            return existing.Trim();
        if (normalizedExisting.Contains(normalizedIncoming, StringComparison.Ordinal))
            return existing.Trim();
        if (normalizedIncoming.Contains(normalizedExisting, StringComparison.Ordinal))
            return incoming.Trim();

        return $"{existing.Trim()}\n\n{incoming.Trim()}";
    }

    private static (string Summary, string? Details) SplitDescription(string description)
    {
        var normalized = description.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return (string.Empty, null);

        var sentenceEnd = FindSentenceBoundary(normalized);
        if (sentenceEnd <= 0 || sentenceEnd >= normalized.Length)
            return (normalized, null);

        var summary = normalized[..sentenceEnd].Trim();
        var details = normalized[sentenceEnd..].Trim();
        return string.IsNullOrWhiteSpace(details)
            ? (summary, null)
            : (summary, details);
    }

    private static int FindSentenceBoundary(string text)
    {
        const int maxSummaryLength = 180;
        for (var index = 0; index < text.Length && index < maxSummaryLength; index++)
        {
            var ch = text[index];
            if (ch is '.' or '!' or '?')
                return index + 1;
        }

        if (text.Length <= maxSummaryLength)
            return text.Length;

        var whitespaceBoundary = text.LastIndexOf(' ', maxSummaryLength);
        return whitespaceBoundary > 0 ? whitespaceBoundary : maxSummaryLength;
    }

    private static string ReadTextUtf8(string path, int maxChars, out bool truncated)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);

        var buffer = new char[maxChars];
        var read = reader.ReadBlock(buffer, 0, maxChars);
        truncated = reader.Peek() >= 0;
        return new string(buffer, 0, read).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
