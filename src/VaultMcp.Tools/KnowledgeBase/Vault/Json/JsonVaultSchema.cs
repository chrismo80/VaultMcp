using System.Text.Json.Serialization;
using VaultMcp.Tools.KnowledgeBase;

namespace VaultMcp.Tools.KnowledgeBase.Vault.Json;

internal sealed record JsonVaultNote(
    string? Schema,
    string? Id,
    string? Kind,
    string? Title,
    string? Summary,
    string? Details,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<string>? Aliases,
    IReadOnlyList<string>? Related,
    string? Confidence,
    IReadOnlyDictionary<string, string>? Scalars,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Lists,
    IReadOnlyList<JsonVaultSection>? Sections,
    IReadOnlyList<JsonVaultLearning>? Learnings,
    JsonVaultMeta? Meta);

internal sealed record JsonVaultSection(
    string? Id,
    string? Title,
    string? Type,
    string? Content,
    IReadOnlyList<string>? Items);

internal sealed record JsonVaultLearning(
    string? Hash,
    DateTimeOffset? CapturedAt,
    string? Summary,
    string? Details,
    IReadOnlyDictionary<string, string>? Scalars,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Lists);

internal sealed record JsonVaultMeta(
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed record JsonParsedNote(
    string RawContent,
    string Title,
    string? Summary,
    string? Details,
    string BodyContent,
    IReadOnlyList<string> Headings,
    VaultMetadata Metadata,
    VaultStructuredContent Structured,
    IReadOnlyList<string> LearningHashes);
