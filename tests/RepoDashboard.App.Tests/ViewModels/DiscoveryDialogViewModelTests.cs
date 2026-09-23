using FluentAssertions;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Discovery;
using RepoDashboard.Core.Repositories;

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
            new HashSet<string>(RepositoryPathComparer.Comparer));

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
            new HashSet<string>(RepositoryPathComparer.Comparer)
            {
                """C:\Source\Repos\Store"""
            });

        var store = sut.Options.First(o => string.Equals(o.Name, "Store", StringComparison.Ordinal));
        store.IsAlreadyTracked.Should().BeTrue();
        store.IsChecked.Should().BeFalse();
        store.IsSelectable.Should().BeFalse();
        store.DisplayText.Should().Contain("already on dashboard");

        sut.SelectedPaths.Should().BeEquivalentTo(
            """C:\Source\Repos\Viewer""");
    }

    [Fact]
    public void Already_tracked_match_is_separator_insensitive_and_os_case_sensitive()
    {
        // RM-004: trailing separators are always ignored; case sensitivity
        // follows the OS (insensitive on Windows, sensitive on Linux).
        // RM-006: the trailing separator must be the OS-native one —
        // backslash is a filename character, not a separator, on Linux.
        var trailingSeparator = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(RepositoryPathComparer.Comparer)
            {
                $"""C:\Source\Repos\Store{Path.DirectorySeparatorChar}"""
            });

        trailingSeparator.Options.Single().IsAlreadyTracked.Should().BeTrue();
        trailingSeparator.SelectedPaths.Should().BeEmpty();

        var differentCase = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(RepositoryPathComparer.Comparer)
            {
                """c:\source\repos\store"""
            });

        if (OperatingSystem.IsWindows())
        {
            differentCase.Options.Single().IsAlreadyTracked.Should().BeTrue();
        }
        else
        {
            differentCase.Options.Single().IsAlreadyTracked.Should().BeFalse();
        }
    }

    [Fact]
    public void Unchecking_removes_from_selection()
    {
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(RepositoryPathComparer.Comparer));

        sut.Options.Single().IsChecked = false;

        sut.SelectedPaths.Should().BeEmpty();
    }

    [Fact]
    public void ToggleSelected_flips_highlighted_selectable_option()
    {
        // Task 52: Space toggles the ListBox-selected row.
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(RepositoryPathComparer.Comparer));
        sut.SelectedOption = sut.Options.Single();

        sut.ToggleSelectedCommand.CanExecute(null).Should().BeTrue();
        sut.ToggleSelectedCommand.Execute(null);

        sut.Options.Single().IsChecked.Should().BeFalse();
        sut.SelectedPaths.Should().BeEmpty();

        sut.ToggleSelectedCommand.Execute(null);

        sut.Options.Single().IsChecked.Should().BeTrue();
    }

    [Fact]
    public void ToggleSelected_is_disabled_without_selectable_highlight()
    {
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(RepositoryPathComparer.Comparer)
            {
                """C:\Source\Repos\Store"""
            });

        // Review #15: constructor preselects the first selectable row
        // (Viewer), so clear it first to cover the nothing-highlighted case.
        sut.SelectedOption = null;
        sut.ToggleSelectedCommand.CanExecute(null).Should().BeFalse(
            "nothing is highlighted");

        sut.SelectedOption = sut.Options.First(o => string.Equals(o.Name, "Store", StringComparison.Ordinal));
        sut.ToggleSelectedCommand.CanExecute(null).Should().BeFalse(
            "already-tracked rows are not selectable");

        // Disabled command must not flip anything even if executed.
        sut.ToggleSelectedCommand.Execute(null);
        sut.Options.First(o => string.Equals(o.Name, "Store", StringComparison.Ordinal)).IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Constructor_preselects_first_selectable_option()
    {
        // Review #15: Discovery must open with a sensible highlight so
        // Space / arrows / Ctrl+A work immediately.
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(RepositoryPathComparer.Comparer)
            {
                """C:\Source\Repos\Store"""
            });

        sut.SelectedOption.Should().Be(sut.Options.First(o => string.Equals(o.Name, "Viewer", StringComparison.Ordinal)));
        sut.ToggleSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Constructor_falls_back_to_first_option_when_all_tracked()
    {
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(RepositoryPathComparer.Comparer)
            {
                """C:\Source\Repos\Store"""
            });

        sut.SelectedOption.Should().Be(sut.Options.Single());
        sut.ToggleSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Constructor_leaves_no_highlight_when_empty()
    {
        var sut = new DiscoveryDialogViewModel(
            [],
            new HashSet<string>(RepositoryPathComparer.Comparer));

        sut.SelectedOption.Should().BeNull();
        sut.ToggleSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SelectAll_checks_only_selectable_options()
    {
        // Task 52: Ctrl+A respects IsSelectable — duplicates stay out.
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(RepositoryPathComparer.Comparer)
            {
                """C:\Source\Repos\Store"""
            });
        sut.Options.First(o => string.Equals(o.Name, "Viewer", StringComparison.Ordinal)).IsChecked = false;

        sut.SelectAllCommand.Execute(null);

        sut.Options.First(o => string.Equals(o.Name, "Viewer", StringComparison.Ordinal)).IsChecked.Should().BeTrue();
        sut.Options.First(o => string.Equals(o.Name, "Store", StringComparison.Ordinal)).IsChecked.Should().BeFalse();
        sut.SelectedPaths.Should().BeEquivalentTo("""C:\Source\Repos\Viewer""");
    }

    [Fact]
    public void ClearSelection_unchecks_only_selectable_options()
    {
        // Task 52: Ctrl+Shift+A clears available rows, never tracked ones.
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(RepositoryPathComparer.Comparer));

        sut.ClearSelectionCommand.Execute(null);

        sut.Options.Should().OnlyContain(o => !o.IsChecked);
        sut.SelectedPaths.Should().BeEmpty();
    }
}
