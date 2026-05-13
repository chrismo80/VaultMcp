using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using VaultMcp.Tools.KnowledgeBase.SemanticIndex;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.KnowledgeBase.Vault.Json;
using VaultMcp.Tools.KnowledgeBase.Search;
using VaultMcp.Tools.KnowledgeBase.Search.Lexical;

namespace VaultMcp.Tools;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVaultMcp(this IServiceCollection services, string rootPath)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var defaultSemanticOptions = CreateSemanticIndexOptions(fullRootPath);

        // Allow callers to override semantic indexing via DI without requiring env vars or CLI flags.
        // If the caller has already registered SemanticIndexOptions / IEmbeddingProvider / ISemanticIndex,
        // we keep their registrations.
        services.TryAdd(ServiceDescriptor.Singleton(defaultSemanticOptions));
        services.TryAddSingleton<IEmbeddingProvider>(provider => CreateEmbeddingProvider(provider.GetRequiredService<SemanticIndexOptions>()));
        services.TryAddSingleton<ISemanticIndex>(provider => new JsonBinarySemanticIndex(
            provider.GetRequiredService<SemanticIndexOptions>(),
            provider.GetRequiredService<IEmbeddingProvider>()));

        services.TryAddSingleton<ISearch, LexicalSearch>();
        services.TryAddSingleton<IVault>(provider => new JsonVault(fullRootPath, provider.GetRequiredService<ISearch>()));

        foreach (var type in GetTools())
            services.AddSingleton(type);

        return services;
    }

    public static IEnumerable<Type> GetTools() => Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(IsTool)
        .Distinct();

    private static bool IsTool(Type type) =>
        type is { IsClass: true, IsAbstract: false } &&
        type.GetCustomAttribute<McpServerToolTypeAttribute>(false) is not null;

    private static SemanticIndexOptions CreateSemanticIndexOptions(string rootPath)
    {
        var providerName = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_PROVIDER")?.Trim();
        var embeddingModel = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL")?.Trim();
        var embeddingModelPath = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_MODEL_PATH")?.Trim();
        var embeddingVocabPath = Environment.GetEnvironmentVariable("VAULTMCP_EMBEDDINGS_VOCAB_PATH")?.Trim();
        var resolvedModel = string.IsNullOrWhiteSpace(embeddingModel) ? "all-MiniLM-L6-v2" : embeddingModel;
        var resolvedModelPath = EmbeddingModelPaths.ResolveModelPath(rootPath, resolvedModel, embeddingModelPath);
        var resolvedVocabPath = EmbeddingModelPaths.ResolveVocabPath(rootPath, resolvedModel, embeddingVocabPath, resolvedModelPath);
        var resolvedProvider = ResolveProviderName(providerName, resolvedModelPath, resolvedVocabPath);

        return new SemanticIndexOptions(
            rootPath,
            Path.Combine(rootPath, ".vault"),
            resolvedProvider,
            resolvedModel,
            resolvedModelPath,
            resolvedVocabPath,
            MaxChunkWords: 350,
            MaxPreviewChars: 240,
            Scoring: SemanticSearchScoringOptions.Default);
    }

    private static IEmbeddingProvider CreateEmbeddingProvider(SemanticIndexOptions options)
        => options.ProviderName.ToLowerInvariant() switch
        {
            "onnx" or "local-onnx" or "local" => CreateOnnxEmbeddingProvider(options),
            "none" or "disabled" => new UnavailableEmbeddingProvider("embedding provider unavailable. Download all-MiniLM-L6-v2 into .vault/models or configure VAULTMCP_EMBEDDINGS_PROVIDER=onnx with VAULTMCP_EMBEDDINGS_MODEL_PATH and VAULTMCP_EMBEDDINGS_VOCAB_PATH.", options.ProviderName, options.EmbeddingModel),
            _ => new UnavailableEmbeddingProvider($"embedding provider '{options.ProviderName}' is not supported yet.", options.ProviderName, options.EmbeddingModel)
        };

    private static string ResolveProviderName(string? configuredProvider, string modelPath, string vocabPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredProvider))
            return configuredProvider;

        return File.Exists(modelPath) && File.Exists(vocabPath)
            ? "onnx"
            : "none";
    }

    private static IEmbeddingProvider CreateOnnxEmbeddingProvider(SemanticIndexOptions options)
        => File.Exists(options.EmbeddingModelPath) && File.Exists(options.EmbeddingVocabPath)
            ? new OnnxBertEmbeddingProvider(options.EmbeddingModelPath, options.EmbeddingVocabPath, options.EmbeddingModel)
            : new UnavailableEmbeddingProvider($"local onnx embedding assets missing. Expected model at '{options.EmbeddingModelPath}' and vocab at '{options.EmbeddingVocabPath}'.", options.ProviderName, options.EmbeddingModel);
}
