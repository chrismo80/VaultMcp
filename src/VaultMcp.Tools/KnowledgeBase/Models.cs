namespace VaultMcp.Tools.KnowledgeBase;

public sealed record VaultStatus(
    string RootPath,
    bool Exists,
    int NoteCount,
    IReadOnlyList<string> SupportedExtensions);

public sealed record VaultNote(
    string Path,
    string Title);

public sealed record VaultNoteDocument(
    string Path,
    string Title,
    string Content,
    bool IsTruncated = false,
    string? Kind = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? Headings = null,
    VaultStructuredContent? Structured = null,
    string? Summary = null,
    string? Details = null);

public sealed record VaultStructuredContent(
    IReadOnlyDictionary<string, string>? Scalars = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Lists = null,
    IReadOnlyList<VaultSection>? Sections = null,
    IReadOnlyList<VaultStructuredLearning>? Learnings = null);

public sealed record VaultSection(
    string Id,
    string Title,
    string Type,
    string? Content = null,
    IReadOnlyList<string>? Items = null);

public sealed record VaultStructuredLearning(
    string Hash,
    DateTimeOffset CapturedAt,
    string Summary,
    string? Details = null,
    IReadOnlyDictionary<string, string>? Scalars = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Lists = null);

public sealed record VaultSearchResult(
    string Path,
    string Title,
    string Excerpt,
    int Score,
    string? Kind = null,
    IReadOnlyList<string>? Tags = null);

public sealed record VaultCaptureResult(
    string Path,
    string Title,
    string Kind,
    bool Created,
    bool Appended,
    bool Unchanged,
    string Message);

public sealed record VaultTermCapture(
    string Term,
    string Description,
    IReadOnlyList<string>? Aliases = null,
    string? Group = null);

public sealed record VaultTermCaptureResult(
    string Path,
    string Title,
    bool Created,
    bool Updated,
    bool Unchanged,
    string Message,
    IReadOnlyList<string>? Aliases = null,
    string? Group = null);

public sealed record VaultLearningCapture(
    string Kind,
    string Title,
    string Summary,
    string? Details,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? Examples = null,
    IReadOnlyList<string>? Steps = null,
    string? Source = null,
    string? Sink = null,
    string? Scope = null,
    string? FailureMode = null,
    string? Symptom = null,
    string? Cause = null,
    string? Fix = null,
    string? Context = null,
    string? Choice = null,
    string? Consequence = null);

internal sealed record VaultMetadata(
    string? Kind,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Related,
    string? Confidence)
{
    public static VaultMetadata Empty { get; } = new(null, [], [], [], null);
}

internal sealed record VaultIndexedNote(
    string FullPath,
    string RelativePath,
    string Title,
    string RawContent,
    string BodyContent,
    IReadOnlyList<string> Headings,
    VaultMetadata Metadata,
    HashSet<string> ExtractedTerms,
    long FileSizeBytes,
    DateTime LastWriteTimeUtc,
    bool IsIndexTruncated);
