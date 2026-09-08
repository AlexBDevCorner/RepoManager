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
        store.IsSelectable.Should().BeFalse();
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

    [Fact]
    public void ToggleSelected_flips_highlighted_selectable_option()
    {
        // Task 52: Space toggles the ListBox-selected row.
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
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
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                """C:\Source\Repos\Store"""
            });

        sut.ToggleSelectedCommand.CanExecute(null).Should().BeFalse(
            "nothing is highlighted");

        sut.SelectedOption = sut.Options.First(o => o.Name == "Store");
        sut.ToggleSelectedCommand.CanExecute(null).Should().BeFalse(
            "already-tracked rows are not selectable");

        // Disabled command must not flip anything even if executed.
        sut.ToggleSelectedCommand.Execute(null);
        sut.Options.First(o => o.Name == "Store").IsChecked.Should().BeFalse();
    }

    [Fact]
    public void SelectAll_checks_only_selectable_options()
    {
        // Task 52: Ctrl+A respects IsSelectable — duplicates stay out.
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                """C:\Source\Repos\Store"""
            });
        sut.Options.First(o => o.Name == "Viewer").IsChecked = false;

        sut.SelectAllCommand.Execute(null);

        sut.Options.First(o => o.Name == "Viewer").IsChecked.Should().BeTrue();
        sut.Options.First(o => o.Name == "Store").IsChecked.Should().BeFalse();
        sut.SelectedPaths.Should().BeEquivalentTo("""C:\Source\Repos\Viewer""");
    }

    [Fact]
    public void ClearSelection_unchecks_only_selectable_options()
    {
        // Task 52: Ctrl+Shift+A clears available rows, never tracked ones.
        var sut = new DiscoveryDialogViewModel(
            [Repo("Store"), Repo("Viewer")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        sut.ClearSelectionCommand.Execute(null);

        sut.Options.Should().OnlyContain(o => !o.IsChecked);
        sut.SelectedPaths.Should().BeEmpty();
    }
}
