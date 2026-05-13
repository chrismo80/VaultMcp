using System.ComponentModel;
using ModelContextProtocol.Server;
using VaultMcp.Tools;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.SemanticIndex;
using VaultMcp.Tools.KnowledgeBase.Vault;

namespace VaultMcp.Tools.Tools;

public sealed record CaptureLearningResponse(
    VaultCaptureResult? Result,
    ErrorInfo? Error = null,
    ErrorInfo? IndexError = null)
{
    public static CaptureLearningResponse AsError(ErrorInfo error) => new(null, error);
}

[McpServerToolType]
public sealed class CaptureLearningTool
{
    private readonly IVault _vault;
    private readonly ISemanticIndex? _semanticIndex;

    public CaptureLearningTool(IVault vault)
        : this(vault, null)
    {
    }

    public CaptureLearningTool(IVault vault, ISemanticIndex? semanticIndex)
    {
        _vault = vault;
        _semanticIndex = semanticIndex;
    }
    [McpServerTool(Name = "capture_learning", Title = "Capture Learning")]
    [Description("Persist durable, repo-relevant domain knowledge learned during work into a controlled structured note. Use for grounded terms, workflows, data flows, invariants, pitfalls, and decisions — not for speculative guesses, temporary task status, duplicates, or raw chat transcript fragments.")]
    public CaptureLearningResponse Execute(
        [Description("Learning kind. Canonical values: term, workflow, data-flow, invariant, pitfall, decision. Aliases like glossary, concept, rule, data_flow, and adr are accepted and normalized.")]
        string kind,
        [Description("Canonical note title, for example 'Order Aggregate' or 'Invoice Correction Flow'.")]
        string title,
        [Description("Short durable summary of what was learned.")]
        string summary,
        [Description("Optional extra detail, examples, or edge cases.")]
        string? details = null,
        [Description("Optional tags to store with the note.")]
        string[]? tags = null,
        [Description("Optional aliases for kind=term.")]
        string[]? aliases = null,
        [Description("Optional examples for kind=term.")]
        string[]? examples = null,
        [Description("Optional ordered steps for kind=workflow or kind=data-flow.")]
        string[]? steps = null,
        [Description("Optional source for kind=data-flow.")]
        string? source = null,
        [Description("Optional sink/target for kind=data-flow.")]
        string? sink = null,
        [Description("Optional scope for kind=invariant.")]
        string? scope = null,
        [Description("Optional failure mode for kind=invariant.")]
        string? failureMode = null,
        [Description("Optional symptom for kind=pitfall.")]
        string? symptom = null,
        [Description("Optional cause for kind=pitfall.")]
        string? cause = null,
        [Description("Optional fix/mitigation for kind=pitfall.")]
        string? fix = null,
        [Description("Optional context for kind=decision.")]
        string? context = null,
        [Description("Optional chosen approach for kind=decision.")]
        string? choice = null,
        [Description("Optional consequence/tradeoff for kind=decision.")]
        string? consequence = null)
    {
        try
        {
            var result = _vault.CaptureLearning(new VaultLearningCapture(
                kind,
                title,
                summary,
                details,
                tags ?? [],
                aliases ?? [],
                examples ?? [],
                steps ?? [],
                source,
                sink,
                scope,
                failureMode,
                symptom,
                cause,
                fix,
                context,
                choice,
                consequence));

            ErrorInfo? indexError = null;
            if (_semanticIndex is not null)
            {
                try
                {
                    _semanticIndex.UpsertFile(result.Path);
                }
                catch (EmbeddingProviderUnavailableException)
                {
                    // Lexical-only setups should still capture knowledge cleanly without noisy index warnings.
                }
                catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or IOException or SemanticIndexException)
                {
                    indexError = VaultToolErrors.FromException(exception);
                }
            }

            return new CaptureLearningResponse(result, IndexError: indexError);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or DirectoryNotFoundException or IOException)
        {
            return CaptureLearningResponse.AsError(VaultToolErrors.FromException(exception));
        }
    }
}
