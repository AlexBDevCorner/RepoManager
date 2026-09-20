namespace RepoDashboard.Core.Repositories;

/// <summary>
/// Centralized filesystem/repository path identity semantics (RM-004).
/// Windows path comparisons remain case-insensitive; Linux (and other
/// non-Windows) path comparisons respect case sensitivity. Use this
/// comparer anywhere paths represent filesystem identity or duplicate
/// detection. Deterministic ordering unrelated to filesystem identity
/// (for example discovery result sorting) intentionally keeps its own
/// ordinal comparison and must not use this for sorting.
/// </summary>
public static class RepositoryPathComparer
{
    /// <summary>
    /// OS-specific equality comparer for normalized paths.
    /// </summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// OS-specific comparison for normalized path strings.
    /// </summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Normalizes a path for identity comparison: full path with trailing
    /// directory separators trimmed.
    /// </summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path.Trim());

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Compares two paths for filesystem identity.
    /// </summary>
    public static bool Equals(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        string normalizedLeft;
        string normalizedRight;

        try
        {
            normalizedLeft = Normalize(left);
        }
        catch
        {
            normalizedLeft = left.Trim();
        }

        try
        {
            normalizedRight = Normalize(right);
        }
        catch
        {
            normalizedRight = right.Trim();
        }

        return string.Equals(normalizedLeft, normalizedRight, Comparison);
    }
}
