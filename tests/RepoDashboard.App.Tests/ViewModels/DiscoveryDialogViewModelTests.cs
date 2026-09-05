using FluentAssertions;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Discovery;

namespace RepoDashboard.App.Tests.ViewModels;

public sealed class DiscoveryDialogViewModelTests
{
    private static DiscoveredRepository Repo(string name) =>
        new() { Path = $"""C:\Source\Repos\{name}""", Name = name };

    [Fact]
    public void Untracked_repositories_are_prechecked()
    {
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        sut.Options.Should().HaveCount(2);
        sut.Options.Should().OnlyContain(o => o.IsChecked);
        sut.Options.Should().OnlyContain(o => !o.IsAlreadyTracked);
        sut.SelectedPaths.Should().BeEquivalentTo(
            """C:\Source\Repos\Store""",
            """C:\Source\Repos\Viewer""");
    }

    [Fact]
    public void Already_tracked_repositories_are_unchecked_and_excluded()
    {
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                """C:\Source\Repos\Store"""
            });

        var store = sut.Options.First(o => o.Name == "Store");
        store.IsAlreadyTracked.Should().BeTrue();
        store.IsChecked.Should().BeFalse();
        store.DisplayText.Should().Contain("already on dashboard");

        sut.SelectedPaths.Should().BeEquivalentTo(
            """C:\Source\Repos\Viewer""");
    }

    [Fact]
    public void Already_tracked_match_is_case_and_separator_insensitive()
    {
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                """c:\source\repos\store\"""
            });

        sut.Options.Single().IsAlreadyTracked.Should().BeTrue();
        sut.SelectedPaths.Should().BeEmpty();
    }

    [Fact]
    public void Unchecking_removes_from_selection()
    {
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        sut.Options.Single().IsChecked = false;

        sut.SelectedPaths.Should().BeEmpty();
    }
}
