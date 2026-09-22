using FluentAssertions;
using RepoDashboard.App.Services;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-008: focused coverage for the version resolution/formatting logic used
/// by the main window title. The helper is pure (no IO beyond the caller's
/// assembly) so development builds with <c>+</c> build metadata stay
/// readable and the title never hard-codes a version string.
/// </summary>
public sealed class AppVersionTests
{
    [Fact]
    public void FormatDisplayVersion_returns_informational_version_as_is()
    {
        AppVersion.FormatDisplayVersion("1.2.3", new Version(9, 9, 9))
            .Should().Be("1.2.3");
    }

    [Fact]
    public void FormatDisplayVersion_strips_build_metadata()
    {
        // SDK default: "1.0.0+<sha>". The hash must not reach the title.
        AppVersion.FormatDisplayVersion("1.0.0+abc123def", null)
            .Should().Be("1.0.0");
    }

    [Fact]
    public void FormatDisplayVersion_keeps_prerelease_suffix_without_metadata()
    {
        AppVersion.FormatDisplayVersion("1.2.3-beta.1+sha.123", null)
            .Should().Be("1.2.3-beta.1");
    }

    [Fact]
    public void FormatDisplayVersion_trims_whitespace()
    {
        AppVersion.FormatDisplayVersion("  1.2.3  ", null)
            .Should().Be("1.2.3");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatDisplayVersion_falls_back_to_assembly_version(string? informational)
    {
        AppVersion.FormatDisplayVersion(informational, new Version(2, 3, 4))
            .Should().Be("2.3.4");
    }

    [Fact]
    public void FormatDisplayVersion_metadata_only_falls_back_to_assembly_version()
    {
        AppVersion.FormatDisplayVersion("+abc123", new Version(1, 0, 0))
            .Should().Be("1.0.0");
    }

    [Fact]
    public void FormatDisplayVersion_returns_unknown_when_no_metadata_available()
    {
        AppVersion.FormatDisplayVersion(null, null)
            .Should().Be(AppVersion.UnknownVersion);
        AppVersion.FormatDisplayVersion("   ", null)
            .Should().Be(AppVersion.UnknownVersion);
    }

    [Fact]
    public void FormatTitle_appends_version_with_product_name()
    {
        var title = AppVersion.FormatTitle("1.2.3");

        title.Should().Contain(AppVersion.ProductName);
        title.Should().Contain("1.2.3");
        title.Should().Be("RepoManager — 1.2.3");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatTitle_without_version_returns_readable_product_name(string? version)
    {
        AppVersion.FormatTitle(version).Should().Be(AppVersion.ProductName);
    }

    [Fact]
    public void ResolveVersion_prefers_informational_version()
    {
        // The App assembly carries SDK-generated informational metadata;
        // the resolved display version must be non-empty and must not
        // contain raw '+' build metadata.
        var version = AppVersion.ResolveVersion(typeof(AppVersion).Assembly);

        version.Should().NotBeNullOrWhiteSpace();
        version.Should().NotContain("+");
    }

    [Fact]
    public void GetWindowTitle_contains_product_name_and_runtime_version()
    {
        var title = AppVersion.GetWindowTitle();

        title.Should().Contain(AppVersion.ProductName);
        title.Should().Contain(AppVersion.ResolveVersion(typeof(AppVersion).Assembly));
        title.Should().NotContain("+");
    }

    [Fact]
    public void GetWindowTitle_defaults_to_app_assembly()
    {
        AppVersion.GetWindowTitle()
            .Should().Be(AppVersion.GetWindowTitle(typeof(AppVersion).Assembly));
    }
}
