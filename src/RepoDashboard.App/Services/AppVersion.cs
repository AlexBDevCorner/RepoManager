using System.Reflection;

namespace RepoDashboard.App.Services;

/// <summary>
/// RM-008: resolves and formats the running application version for display
/// in the main window title. Pure formatting/resolution — no Git, no IO,
/// no configuration. Reads standard .NET assembly metadata at runtime
/// (<see cref="AssemblyInformationalVersionAttribute"/>) with a fallback to
/// the assembly version, so development builds with build metadata remain
/// readable.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Product name kept in the main window title.
    /// </summary>
    public const string ProductName = "RepoManager";

    /// <summary>
    /// Fallback display version when no assembly metadata is available.
    /// </summary>
    public const string UnknownVersion = "0.0.0";

    /// <summary>
    /// Formats a raw informational version plus an assembly-version fallback
    /// into a concise, human-readable display version. Strips <c>+</c> build
    /// metadata (commit hash, etc.) while preserving prerelease suffixes
    /// (for example <c>1.2.3-beta.1+abc</c> becomes <c>1.2.3-beta.1</c>).
    /// Pure function: no IO, no reflection.
    /// </summary>
    public static string FormatDisplayVersion(
        string? informationalVersion,
        Version? assemblyVersion)
    {
        var candidate = informationalVersion?.Trim();

        if (!string.IsNullOrEmpty(candidate))
        {
            var plusIndex = candidate.IndexOf('+', StringComparison.Ordinal);

            if (plusIndex >= 0)
            {
                candidate = candidate.Substring(0, plusIndex).Trim();
            }

            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        if (assemblyVersion is not null)
        {
            return assemblyVersion.ToString();
        }

        return UnknownVersion;
    }

    /// <summary>
    /// Formats a window title from an already-resolved display version.
    /// Never emits a dangling separator: an empty version yields just the
    /// product name so the title stays readable.
    /// </summary>
    public static string FormatTitle(string? displayVersion)
    {
        if (string.IsNullOrWhiteSpace(displayVersion))
        {
            return ProductName;
        }

        return $"{ProductName} — {displayVersion.Trim()}";
    }

    /// <summary>
    /// Resolves the display version for the given assembly from runtime
    /// metadata: informational version first, assembly version as fallback.
    /// </summary>
    public static string ResolveVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var assemblyVersion = assembly.GetName().Version;

        return FormatDisplayVersion(informational, assemblyVersion);
    }

    /// <summary>
    /// Resolves the full main-window title for the given assembly. Defaults
    /// to the RepoDashboard.App assembly so unit-test runners (whose entry
    /// assembly is the test host) still report the application version.
    /// </summary>
    public static string GetWindowTitle(Assembly? assembly = null)
    {
        assembly ??= typeof(AppVersion).Assembly;
        return FormatTitle(ResolveVersion(assembly));
    }
}
