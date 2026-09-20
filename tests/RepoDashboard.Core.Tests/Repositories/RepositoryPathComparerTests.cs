using FluentAssertions;
using RepoDashboard.Core.Repositories;

namespace RepoDashboard.Core.Tests.Repositories;

/// <summary>
/// RM-004: path identity is OS-specific (case-insensitive on Windows,
/// case-sensitive on Linux). Normalization always trims trailing separators
/// via full paths.
/// </summary>
public sealed class RepositoryPathComparerTests
{
    [Fact]
    public void Comparer_matches_os_case_sensitivity()
    {
        if (OperatingSystem.IsWindows())
        {
            RepositoryPathComparer.Comparer.Should().Be(StringComparer.OrdinalIgnoreCase);
            RepositoryPathComparer.Comparison.Should().Be(StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            RepositoryPathComparer.Comparer.Should().Be(StringComparer.Ordinal);
            RepositoryPathComparer.Comparison.Should().Be(StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Normalize_trims_trailing_separators()
    {
        var withSeparator = Path.Combine("a", "b") + Path.DirectorySeparatorChar;
        var withoutSeparator = Path.Combine("a", "b");

        // Both normalize through GetFullPath; the comparison below uses the
        // OS-specific comparer so it holds on Windows and Linux.
        RepositoryPathComparer.Normalize(withSeparator)
            .Should().Be(RepositoryPathComparer.Normalize(withoutSeparator));
    }

    [Fact]
    public void Equals_uses_os_specific_case_sensitivity()
    {
        var lower = Path.Combine(Path.GetTempPath(), "repomanager-case-test");
        var upper = lower.ToUpperInvariant();

        var expected = OperatingSystem.IsWindows();

        RepositoryPathComparer.Equals(lower, upper).Should().Be(expected);
    }

    [Fact]
    public void Equals_null_handling()
    {
        RepositoryPathComparer.Equals(null, null).Should().BeTrue();
        RepositoryPathComparer.Equals("a", null).Should().BeFalse();
        RepositoryPathComparer.Equals(null, "a").Should().BeFalse();
    }
}
