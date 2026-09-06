using System.Text.RegularExpressions;

namespace RepoDashboard.Core.Git;

/// <summary>
/// Defense-in-depth for structured logging (Task 41 review): Git stderr can
/// embed an HTTPS remote URL with credentials
/// (<c>https://user:token@host/...</c>). Raw Git output belongs in the UI
/// details model — never in application logs. This sanitizer redacts URI
/// user-info (<c>scheme://anything@</c> → <c>scheme://***@</c>) for the rare
/// cases where a dynamic string is still logged. Prefer logging the
/// classified <see cref="GitFailureKind"/> instead of any raw text.
/// </summary>
public static partial class GitLogSanitizer
{
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return UriUserInfoRegex().Replace(value, "://***@");
    }

    [GeneratedRegex(@"://[^/\s@]+@", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UriUserInfoRegex();
}
