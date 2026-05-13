using System.Numerics.Tensors;
using System.Text.Json;
using VaultMcp.Tools.KnowledgeBase.Search.Lexical;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.KnowledgeBase.Vault.Json;

namespace VaultMcp.Tools.KnowledgeBase.SemanticIndex;

public sealed class JsonBinarySemanticIndex : ISemanticIndex
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly SemanticIndexOptions _options;
    private readonly IEmbeddingProvider _embeddingProvider;
    private SemanticIndexSnapshot? _snapshot;

    public JsonBinarySemanticIndex(SemanticIndexOptions options, IEmbeddingProvider embeddingProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(embeddingProvider);

        _options = options;
        _embeddingProvider = embeddingProvider;
    }

    public void Rebuild()
    {
        EnsureProviderConfigured();

        lock (_sync)
        {
            var snapshot = BuildSnapshot(EnumerateJsonFiles());
            SaveSnapshot(snapshot);
            _snapshot = snapshot;
        }
    }

    public void UpsertFile(string relativePath)
    {
        EnsureProviderConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        lock (_sync)
        {
            var current = LoadSnapshotOrEmpty();
            var fullPath = ResolvePath(relativePath);
            var normalizedRelativePath = Path.GetRelativePath(_options.RootPath, fullPath);
            if (!File.Exists(fullPath))
            {
                DeleteFileInternal(normalizedRelativePath, current);
                return;
            }

            var rawContent = File.ReadAllText(fullPath);
            var contentHash = VaultNoteChunker.Sha256Hex(rawContent);
            if (current.State.Files.TryGetValue(normalizedRelativePath, out var state) && string.Equals(state.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            {
                _snapshot = current;
                return;
            }

            var updated = ReplaceFile(current, normalizedRelativePath, rawContent, File.GetLastWriteTimeUtc(fullPath), DateTimeOffset.UtcNow);
            SaveSnapshot(updated);
            _snapshot = updated;
        }
    }

    public void DeleteFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        lock (_sync)
        {
            var fullPath = ResolvePath(relativePath);
            DeleteFileInternal(Path.GetRelativePath(_options.RootPath, fullPath), LoadSnapshotOrEmpty());
        }
    }

    public IReadOnlyList<SemanticSearchHit> Search(string query, int limit = 10)
    {
        EnsureProviderConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be greater than zero.");

        lock (_sync)
        {
            var snapshot = LoadSnapshot() ?? throw new SemanticIndexNotBuiltException("semantic index not built. Call reindex_vault first.");
            EnsureModelCompatibility(snapshot.Index);

            var queryEmbedding = _embeddingProvider.Embed(query);
            if (queryEmbedding.Length != snapshot.Index.EmbeddingDimensions)
            {
                throw new SemanticIndexModelMismatchException(
                    $"semantic index model mismatch: index expects {snapshot.Index.EmbeddingDimensions} dimensions, provider returned {queryEmbedding.Length}. Rebuild the index.");
            }

            var queryTerms = query.ExtractTerms();

            return snapshot.Index.Chunks
                .Select((chunk, index) => new
                {
                    Chunk = chunk,
                    Score = ScoreChunk(chunk, snapshot.Vectors[index], query, queryTerms, queryEmbedding)
                })
                .Where(x => x.Score > 0f)
                .GroupBy(x => x.Chunk.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Chunk.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Chunk.Path, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(x => new SemanticSearchHit(x.Chunk.Id, x.Chunk.Path, x.Chunk.Title, x.Chunk.Heading, x.Score, x.Chunk.TextPreview))
                .ToArray();
        }
    }

    public SemanticIndexStatus GetStatus()
    {
        lock (_sync)
        {
            try
            {
                var snapshot = LoadSnapshot();
                return BuildStatus(snapshot, warning: null);
            }
            catch (SemanticIndexException exception)
            {
                return BuildStatus(snapshot: null, warning: exception.Message);
            }
            catch (IOException exception)
            {
                return BuildStatus(snapshot: null, warning: $"semantic index io error: {exception.Message}");
            }
        }
    }

    private void DeleteFileInternal(string relativePath, SemanticIndexSnapshot snapshot)
    {
        var updatedChunks = snapshot.Index.Chunks
            .Zip(snapshot.Vectors, (chunk, vector) => (Chunk: chunk, Vector: vector))
            .Where(x => !string.Equals(x.Chunk.Path, relativePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (updatedChunks.Length == snapshot.Index.Chunks.Count && !snapshot.State.Files.ContainsKey(relativePath))
        {
            _snapshot = snapshot;
            return;
        }

        var state = snapshot.State.Files
            .Where(pair => !string.Equals(pair.Key, relativePath, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var updated = RebuildSnapshot(snapshot.Index.ProviderName, snapshot.Index.EmbeddingModel, updatedChunks, state, snapshot.Index.GeneratedAt);
        SaveSnapshot(updated);
        _snapshot = updated;
    }

    private SemanticIndexSnapshot ReplaceFile(SemanticIndexSnapshot snapshot, string relativePath, string rawContent, DateTime modifiedAtUtc, DateTimeOffset indexedAt)
    {
        var preserved = snapshot.Index.Chunks
            .Zip(snapshot.Vectors, (chunk, vector) => (Chunk: chunk, Vector: vector))
            .Where(x => !string.Equals(x.Chunk.Path, relativePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var newChunks = BuildChunkEntries(relativePath, rawContent, modifiedAtUtc, indexedAt);
        preserved.AddRange(newChunks);

        var state = snapshot.State.Files
            .Where(pair => !string.Equals(pair.Key, relativePath, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        state[relativePath] = new IndexedFileState(VaultNoteChunker.Sha256Hex(rawContent), indexedAt);

        return RebuildSnapshot(_embeddingProvider.ProviderName, _embeddingProvider.ModelName, preserved, state, indexedAt);
    }

    private SemanticIndexSnapshot BuildSnapshot(IEnumerable<string> files)
    {
        var indexedAt = DateTimeOffset.UtcNow;
        var entries = new List<(ChunkIndexEntry Chunk, float[] Vector)>();
        var state = new Dictionary<string, IndexedFileState>(StringComparer.OrdinalIgnoreCase);

        foreach (var fullPath in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(_options.RootPath, fullPath);
            var rawContent = File.ReadAllText(fullPath);
            entries.AddRange(BuildChunkEntries(relativePath, rawContent, File.GetLastWriteTimeUtc(fullPath), indexedAt));
            state[relativePath] = new IndexedFileState(VaultNoteChunker.Sha256Hex(rawContent), indexedAt);
        }

        return RebuildSnapshot(_embeddingProvider.ProviderName, _embeddingProvider.ModelName, entries, state, indexedAt);
    }

    private List<(ChunkIndexEntry Chunk, float[] Vector)> BuildChunkEntries(string relativePath, string rawContent, DateTime modifiedAtUtc, DateTimeOffset indexedAt)
    {
        var parsed = JsonVaultParser.Parse(rawContent, Path.GetFileNameWithoutExtension(relativePath));
        var chunks = VaultNoteChunker.Chunk(relativePath, parsed, modifiedAtUtc, _options.MaxChunkWords, _options.MaxPreviewChars);
        var results = new List<(ChunkIndexEntry Chunk, float[] Vector)>();

        foreach (var chunk in chunks)
        {
            var vector = _embeddingProvider.Embed(chunk.Text);
            if (vector.Length == 0)
                throw new EmbeddingProviderUnavailableException("embedding provider returned an empty embedding.");

            results.Add((
                new ChunkIndexEntry(
                    chunk.Id,
                    chunk.Path,
                    chunk.Title,
                    chunk.Heading,
                    chunk.TextPreview,
                    chunk.Tags,
                    chunk.Aliases,
                    chunk.ContentHash,
                    0,
                    vector.Length),
                vector));
        }

        return results;
    }

    private SemanticIndexSnapshot RebuildSnapshot(
        string providerName,
        string embeddingModel,
        IEnumerable<(ChunkIndexEntry Chunk, float[] Vector)> entries,
        IReadOnlyDictionary<string, IndexedFileState> state,
        DateTimeOffset generatedAt)
    {
        var chunks = new List<ChunkIndexEntry>();
        var vectors = new List<float[]>();
        var embeddingOffset = 0;
        int? dimensions = null;

        foreach (var entry in entries.OrderBy(x => x.Chunk.Path, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Chunk.Id, StringComparer.OrdinalIgnoreCase))
        {
            dimensions ??= entry.Vector.Length;
            if (dimensions.Value != entry.Vector.Length)
            {
                throw new SemanticIndexModelMismatchException(
                    $"embedding dimension mismatch while building semantic index: expected {dimensions.Value}, got {entry.Vector.Length}.");
            }

            chunks.Add(entry.Chunk with { EmbeddingOffset = embeddingOffset, EmbeddingLength = entry.Vector.Length });
            vectors.Add(entry.Vector);
            embeddingOffset += entry.Vector.Length;
        }

        var indexFile = new SemanticIndexFile(
            SchemaVersion,
            providerName,
            embeddingModel,
            dimensions ?? 0,
            generatedAt,
            chunks);

        var stateFile = new SemanticIndexStateFile(SchemaVersion, new Dictionary<string, IndexedFileState>(state, StringComparer.OrdinalIgnoreCase));
        return new SemanticIndexSnapshot(indexFile, stateFile, vectors);
    }

    private SemanticIndexSnapshot LoadSnapshotOrEmpty()
        => LoadSnapshot() ?? new SemanticIndexSnapshot(
            new SemanticIndexFile(SchemaVersion, _embeddingProvider.ProviderName, _embeddingProvider.ModelName, 0, DateTimeOffset.MinValue, []),
            new SemanticIndexStateFile(SchemaVersion, new Dictionary<string, IndexedFileState>(StringComparer.OrdinalIgnoreCase)),
            []);

    private SemanticIndexSnapshot? LoadSnapshot()
    {
        if (_snapshot is not null)
            return _snapshot;

        if (!File.Exists(_options.IndexFilePath) || !File.Exists(_options.VectorFilePath) || !File.Exists(_options.StateFilePath))
            return null;

        var index = JsonSerializer.Deserialize<SemanticIndexFile>(File.ReadAllText(_options.IndexFilePath), SerializerOptions)
            ?? throw new SemanticIndexCorruptException("semantic index metadata file is empty or invalid.");

        var state = JsonSerializer.Deserialize<SemanticIndexStateFile>(File.ReadAllText(_options.StateFilePath), SerializerOptions)
            ?? throw new SemanticIndexCorruptException("semantic index state file is empty or invalid.");

        if (index.SchemaVersion != SchemaVersion || state.SchemaVersion != SchemaVersion)
            throw new SemanticIndexModelMismatchException("semantic index schema version mismatch. Rebuild the index.");

        var vectors = ReadVectors(index);
        _snapshot = new SemanticIndexSnapshot(index, state, vectors);
        return _snapshot;
    }

    private IReadOnlyList<float[]> ReadVectors(SemanticIndexFile index)
    {
        using var stream = File.OpenRead(_options.VectorFilePath);
        using var reader = new BinaryReader(stream);

        var vectors = new List<float[]>(index.Chunks.Count);
        var expectedOffset = 0;

        foreach (var chunk in index.Chunks)
        {
            if (chunk.EmbeddingOffset != expectedOffset)
                throw new SemanticIndexCorruptException($"semantic vector offsets are corrupt for chunk '{chunk.Id}'.");

            var vector = new float[chunk.EmbeddingLength];
            for (var i = 0; i < vector.Length; i++)
            {
                if (stream.Position >= stream.Length)
                    throw new SemanticIndexCorruptException("semantic vector file ended unexpectedly.");

                vector[i] = reader.ReadSingle();
            }

            vectors.Add(vector);
            expectedOffset += chunk.EmbeddingLength;
        }

        if (stream.Position != stream.Length)
            throw new SemanticIndexCorruptException("semantic vector file contains trailing data.");

        return vectors;
    }

    private void SaveSnapshot(SemanticIndexSnapshot snapshot)
    {
        Directory.CreateDirectory(_options.IndexDirectory);

        var indexTemp = _options.IndexFilePath + ".tmp";
        var vectorsTemp = _options.VectorFilePath + ".tmp";
        var stateTemp = _options.StateFilePath + ".tmp";

        File.WriteAllText(indexTemp, JsonSerializer.Serialize(snapshot.Index, SerializerOptions));
        File.WriteAllText(stateTemp, JsonSerializer.Serialize(snapshot.State, SerializerOptions));

        using (var stream = File.Create(vectorsTemp))
        using (var writer = new BinaryWriter(stream))
        {
            foreach (var vector in snapshot.Vectors)
            {
                foreach (var value in vector)
                    writer.Write(value);
            }
        }

        ReplaceAtomically(indexTemp, _options.IndexFilePath);
        ReplaceAtomically(vectorsTemp, _options.VectorFilePath);
        ReplaceAtomically(stateTemp, _options.StateFilePath);
    }

    private SemanticIndexStatus BuildStatus(SemanticIndexSnapshot? snapshot, string? warning)
        => new(
            _options.RootPath,
            _options.IndexDirectory,
            _embeddingProvider.IsConfigured,
            _embeddingProvider.ProviderName,
            _embeddingProvider.ModelName,
            snapshot is not null,
            snapshot?.Index.EmbeddingModel,
            snapshot?.Index.EmbeddingDimensions > 0 ? snapshot.Index.EmbeddingDimensions : null,
            snapshot?.Index.Chunks.Count ?? 0,
            snapshot?.State.Files.Count ?? 0,
            snapshot?.State.Files.Count > 0 ? snapshot.State.Files.Values.Max(file => file.IndexedAt) : null,
            warning);

    private void EnsureProviderConfigured()
    {
        if (!_embeddingProvider.IsConfigured)
            throw new EmbeddingProviderUnavailableException("embedding provider unavailable. Configure an embedding provider (via DI or configuration) before semantic indexing.");
    }

    private void EnsureModelCompatibility(SemanticIndexFile index)
    {
        if (!string.Equals(index.ProviderName, _embeddingProvider.ProviderName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(index.EmbeddingModel, _embeddingProvider.ModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new SemanticIndexModelMismatchException(
                $"semantic index model mismatch: index was built with {index.ProviderName}/{index.EmbeddingModel}, but provider is {_embeddingProvider.ProviderName}/{_embeddingProvider.ModelName}. Rebuild the index.");
        }
    }

    private IEnumerable<string> EnumerateJsonFiles()
    {
        if (!Directory.Exists(_options.RootPath))
            return [];

        return Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                if (path.Contains(Path.Combine(_options.RootPath, ".vault"), StringComparison.OrdinalIgnoreCase))
                    return false;

                var extension = Path.GetExtension(path);
                return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
            });
    }

    private string ResolvePath(string relativePath)
        => VaultPathGuard.ResolvePath(_options.RootPath, relativePath);

    private static void ReplaceAtomically(string tempPath, string finalPath)
        => File.Move(tempPath, finalPath, overwrite: true);

    private float ScoreChunk(ChunkIndexEntry chunk, float[] vector, string query, HashSet<string> queryTerms, float[] queryEmbedding)
    {
        var semanticScore = CosineSimilarity(queryEmbedding, vector);
        var lexicalScore = ComputeLexicalScore(queryTerms, chunk);
        var metadataBoost = ComputeMetadataBoost(query, chunk);
        var scoring = _options.EffectiveScoring;

        return (scoring.SemanticWeight * semanticScore) +
               (scoring.LexicalWeight * lexicalScore) +
               (scoring.MetadataWeight * metadataBoost);
    }

    private static float ComputeLexicalScore(HashSet<string> queryTerms, ChunkIndexEntry chunk)
    {
        if (queryTerms.Count == 0)
            return 0f;

        var haystack = string.Join(
                Environment.NewLine,
                new[]
                {
                    chunk.Path,
                    chunk.Title,
                    chunk.Heading ?? string.Empty,
                    chunk.TextPreview,
                    string.Join(Environment.NewLine, chunk.Tags),
                    string.Join(Environment.NewLine, chunk.Aliases)
                })
            .ExtractTerms();

        var matches = queryTerms.Count(term => haystack.Contains(term));
        return matches / (float)queryTerms.Count;
    }

    private static float ComputeMetadataBoost(string query, ChunkIndexEntry chunk)
    {
        var score = 0f;
        if (string.Equals(chunk.Title, query, StringComparison.OrdinalIgnoreCase))
            score += 0.6f;
        else if (chunk.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 0.3f;

        if (chunk.Aliases.Any(alias => string.Equals(alias, query, StringComparison.OrdinalIgnoreCase)))
            score += 0.5f;
        else if (chunk.Aliases.Any(alias => alias.Contains(query, StringComparison.OrdinalIgnoreCase)))
            score += 0.2f;

        if (chunk.Tags.Any(tag => string.Equals(tag, query, StringComparison.OrdinalIgnoreCase)))
            score += 0.2f;

        if (chunk.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 0.15f;

        return Math.Min(1f, score);
    }

    internal static float CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Vector dimensions must match for cosine similarity.");

        var dot = TensorPrimitives.Dot(left, right);
        var leftNorm = TensorPrimitives.Dot(left, left);
        var rightNorm = TensorPrimitives.Dot(right, right);

        if (leftNorm == 0 || rightNorm == 0)
            return 0;

        return dot / (MathF.Sqrt(leftNorm) * MathF.Sqrt(rightNorm));
    }
}
