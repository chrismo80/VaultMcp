namespace VaultMcp.Tools.KnowledgeBase.Vault;

internal static class VaultPathGuard
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static string ResolvePath(string rootPath, string relativePath, params string[] allowedExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedRootPath = Path.GetFullPath(rootPath);
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (Path.IsPathRooted(normalizedRelativePath) || LooksLikeWindowsAbsolutePath(normalizedRelativePath))
            throw new ArgumentException("Only vault-relative note paths are allowed.", nameof(relativePath));

        var fullPath = Path.GetFullPath(Path.Combine(normalizedRootPath, normalizedRelativePath));
        var rootPrefix = normalizedRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRootPath
            : normalizedRootPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, PathComparison) &&
            !string.Equals(fullPath, normalizedRootPath, PathComparison))
        {
            throw new ArgumentException("The requested note path escapes the configured vault root.", nameof(relativePath));
        }

        if (allowedExtensions.Length > 0 &&
            !allowedExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only configured vault note paths are allowed.", nameof(relativePath));
        }

        return fullPath;
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static bool LooksLikeWindowsAbsolutePath(string path)
        => path.Length >= 3 &&
           char.IsLetter(path[0]) &&
           path[1] == ':' &&
           (path[2] == Path.DirectorySeparatorChar || path[2] == '/');
}
