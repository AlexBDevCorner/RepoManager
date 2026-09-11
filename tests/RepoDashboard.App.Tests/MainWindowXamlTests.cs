using System.Xml.Linq;
using FluentAssertions;

namespace RepoDashboard.App.Tests;

/// <summary>
/// Task 50 regression coverage for <c>MainWindow.xaml</c> itself.
/// A pure view-model test cannot catch these: WPF sorting happens in
/// the collection view, outside the view model.
/// Tasks 51–53 extend this with keyboard-shortcut contract tests: the
/// keymap is a UI concern validated by parsing XAML, not by spinning up
/// WPF automation.
/// </summary>
public sealed class MainWindowXamlTests
{
    private static string FindXaml(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "RepoDashboard.App",
                fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate src/RepoDashboard.App/{fileName} from "
            + AppContext.BaseDirectory);
    }

    private static string FindMainWindowXaml() => FindXaml("MainWindow.xaml");

    private static XDocument LoadMainWindow() =>
        XDocument.Load(FindMainWindowXaml());

    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Task 51: every Window-level KeyBinding, with the command text as
    /// written in XAML. F1 binds the routed Help command; everything else
    /// binds a view-model command.
    /// </summary>
    private static IEnumerable<XElement> WindowKeyBindings(XDocument document) =>
        document.Root
            ?.Element(Presentation + "Window.InputBindings")
            ?.Elements(Presentation + "KeyBinding")
            ?? [];

    [Fact]
    public void MainWindow_DataGrid_disables_user_sorting_and_stays_read_only()
    {
        // Task 50: custom order is the dashboard order. An active DataGrid
        // sort would display positions different from the
        // ObservableCollection indexes Move Up/Down persist, silently
        // saving an order the user never saw.
        var document = LoadMainWindow();

        var grid = document
            .Descendants(Presentation + "DataGrid")
            .Single();

        grid.Attribute("CanUserSortColumns")?.Value.Should().Be("False");
        grid.Attribute("IsReadOnly")?.Value.Should().Be("True");
    }

    [Fact]
    public void MainWindow_repository_grid_is_named_for_initial_focus()
    {
        // Task 52: MainWindow.xaml.cs focuses RepositoryGrid on Loaded so
        // keyboard navigation works immediately, even while the async
        // load is still in flight.
        var document = LoadMainWindow();

        var grid = document
            .Descendants(Presentation + "DataGrid")
            .Single();

        grid.Attribute(XamlNamespace + "Name")?.Value.Should().Be("RepositoryGrid");
    }

    [Fact]
    public void MainWindow_registers_help_command_binding_for_F1()
    {
        // Task 53: F1 opens the shortcut reference through the routed
        // Help command — no help state in the view model.
        var document = LoadMainWindow();

        var bindings = document.Root
            ?.Element(Presentation + "Window.CommandBindings")
            ?.Elements(Presentation + "CommandBinding")
            .ToList() ?? [];

        bindings
            .Where(b => CommandValue(b).Contains("ApplicationCommands.Help"))
            .Should().ContainSingle();
    }

    private static string CommandValue(XElement element) =>
        element.Attribute("Command") is { } attribute
            ? attribute.Value
            : string.Empty;

    private static string GestureValue(XElement element) =>
        element.Attribute("Gesture") is { } attribute
            ? attribute.Value
            : string.Empty;

    [Theory]
    [InlineData("Ctrl+N", "AddCommand")]
    [InlineData("Ctrl+Shift+N", "DiscoverCommand")]
    [InlineData("F2", "RenameCommand")]
    [InlineData("Alt+Up", "MoveUpCommand")]
    [InlineData("Alt+Down", "MoveDownCommand")]
    [InlineData("Delete", "RemoveCommand")]
    [InlineData("F5", "RefreshCommand")]
    [InlineData("Shift+F5", "RefreshAllCommand")]
    [InlineData("F6", "FetchCommand")]
    [InlineData("Shift+F6", "FetchAllCommand")]
    [InlineData("Ctrl+F7", "UpdateCommand")]
    [InlineData("Ctrl+Shift+F7", "UpdateAllCommand")]
    [InlineData("Escape", "CancelCommand")]
    [InlineData("Enter", "OpenFolderCommand")]
    [InlineData("Ctrl+Enter", "OpenTerminalCommand")]
    [InlineData("Ctrl+Shift+C", "CopyPathCommand")]
    [InlineData("F1", "ApplicationCommands.Help")]
    public void MainWindow_registers_expected_shortcut(
        string gesture,
        string command)
    {
        // Task 51/53: the shortcut layer must call the existing commands
        // directly — no parallel handlers, no Git logic in the view.
        var document = LoadMainWindow();

        var match = WindowKeyBindings(document).FirstOrDefault(k =>
            string.Equals(
                k.Attribute("Gesture")?.Value, gesture,
                StringComparison.OrdinalIgnoreCase)
            && (k.Attribute("Command")?.Value ?? string.Empty).Contains(
                command, StringComparison.Ordinal));

        match.Should().NotBeNull(
            $"expected KeyBinding Gesture=\"{gesture}\" Command containing \"{command}\"");
    }

    [Fact]
    public void MainWindow_keyboard_shortcuts_are_unique()
    {
        // Task 53: duplicate gestures would make one shortcut unreachable.
        var document = LoadMainWindow();

        var gestures = WindowKeyBindings(document)
            .Select(k => k.Attribute("Gesture")?.Value ?? string.Empty)
            .ToList();

        gestures.Should().NotBeEmpty();
        gestures.Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().HaveCount(gestures.Count);
    }

    [Fact]
    public void MainWindow_keyboard_shortcut_set_matches_expected_keymap()
    {
        // Task 53: expected-set guard — adding, removing or retyping a
        // gesture fails loudly instead of silently changing the keymap.
        var expected = new[]
        {
            "Ctrl+N",
            "Ctrl+Shift+N",
            "F2",
            "Alt+Up",
            "Alt+Down",
            "Delete",
            "F5",
            "Shift+F5",
            "F6",
            "Shift+F6",
            "Ctrl+F7",
            "Ctrl+Shift+F7",
            "Escape",
            "Enter",
            "Ctrl+Enter",
            "Ctrl+Shift+C",
            "F1"
        };

        var actual = WindowKeyBindings(LoadMainWindow())
            .Select(k => k.Attribute("Gesture")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        actual.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void MainWindow_shortcuts_use_selection_instead_of_command_parameter()
    {
        // Task 51: no CommandParameter on purpose. A null parameter means
        // "use the selection", matching toolbar buttons. Context-menu
        // items keep passing the right-clicked row explicitly.
        var document = LoadMainWindow();

        foreach (var binding in WindowKeyBindings(document))
        {
            binding.Attribute("CommandParameter").Should().BeNull(
                $"Gesture \"{binding.Attribute("Gesture")?.Value}\" must not carry a CommandParameter");
        }
    }

    [Theory]
    [InlineData("Refresh", "F5")]
    [InlineData("Fetch", "F6")]
    [InlineData("Update", "Ctrl+F7")]
    [InlineData("Open Folder", "Enter")]
    [InlineData("Open Terminal", "Ctrl+Enter")]
    [InlineData("Copy Path", "Ctrl+Shift+C")]
    [InlineData("Rename...", "F2")]
    [InlineData("Move Up", "Alt+Up")]
    [InlineData("Move Down", "Alt+Down")]
    [InlineData("Remove from dashboard", "Delete")]
    public void MainWindow_context_menu_reveals_shortcut(
        string header,
        string gesture)
    {
        // Task 53: InputGestureText is display-only; the KeyBindings above
        // remain the functional source.
        var document = LoadMainWindow();

        var item = document
            .Descendants(Presentation + "MenuItem")
            .FirstOrDefault(m =>
                string.Equals(
                    m.Attribute("Header")?.Value, header,
                    StringComparison.Ordinal));

        item.Should().NotBeNull($"expected context-menu item \"{header}\"");
        item!.Attribute("InputGestureText")?.Value.Should().Be(gesture);
    }

    [Theory]
    [InlineData("AddCommand", "Ctrl+N")]
    [InlineData("DiscoverCommand", "Ctrl+Shift+N")]
    [InlineData("RenameCommand", "F2")]
    [InlineData("MoveUpCommand", "Alt+Up")]
    [InlineData("MoveDownCommand", "Alt+Down")]
    [InlineData("RemoveCommand", "Delete")]
    [InlineData("RefreshCommand", "F5")]
    [InlineData("RefreshAllCommand", "Shift+F5")]
    [InlineData("FetchCommand", "F6")]
    [InlineData("FetchAllCommand", "Shift+F6")]
    [InlineData("UpdateCommand", "Ctrl+F7")]
    [InlineData("UpdateAllCommand", "Ctrl+Shift+F7")]
    [InlineData("CancelCommand", "Esc")]
    [InlineData("OpenFolderCommand", "Enter")]
    [InlineData("OpenTerminalCommand", "Ctrl+Enter")]
    [InlineData("CopyPathCommand", "Ctrl+Shift+C")]
    public void MainWindow_toolbar_tooltip_reveals_shortcut(
        string command,
        string gesture)
    {
        // Task 53: mouse users discover shortcuts gradually via tooltips.
        var document = LoadMainWindow();

        var button = document
            .Descendants(Presentation + "Button")
            .FirstOrDefault(b =>
                (b.Attribute("Command")?.Value ?? string.Empty).Contains(
                    command, StringComparison.Ordinal));

        button.Should().NotBeNull($"expected toolbar button for {command}");
        button!.Attribute("ToolTip")?.Value.Should().Contain(gesture);
    }

    [Fact]
    public void DiscoveryDialog_supports_keyboard_checklist_operations()
    {
        // Task 52: arrows move (native ListBox), Space toggles, Ctrl+A
        // selects all available, Ctrl+Shift+A clears. Enter/Esc are
        // covered by IsDefault/IsCancel buttons.
        // Review #15: the ListBox is named so the dialog can focus it on
        // Loaded — ListBox.InputBindings only work when focus is inside.
        var document = XDocument.Load(FindXaml("DiscoveryDialog.xaml"));

        var listBox = document
            .Descendants(Presentation + "ListBox")
            .Single();

        listBox.Attribute(XamlNamespace + "Name")?.Value.Should().Be("RepositoryList");
        listBox.Attribute("SelectedItem")?.Value.Should().Contain("SelectedOption");

        var bindings = listBox
            .Element(Presentation + "ListBox.InputBindings")
            ?.Elements(Presentation + "KeyBinding")
            .ToList() ?? [];

        bindings
            .Where(b => GestureValue(b) == "Space" && CommandValue(b).Contains("ToggleSelectedCommand"))
            .Should().ContainSingle();
        bindings
            .Where(b => GestureValue(b) == "Ctrl+A" && CommandValue(b).Contains("SelectAllCommand"))
            .Should().ContainSingle();
        bindings
            .Where(b => GestureValue(b) == "Ctrl+Shift+A" && CommandValue(b).Contains("ClearSelectionCommand"))
            .Should().ContainSingle();

        var checkBox = document
            .Descendants(Presentation + "CheckBox")
            .Single();

        checkBox.Attribute("Focusable")?.Value.Should().Be("False");
    }

    [Fact]
    public void DiscoveryDialog_focuses_checklist_on_open()
    {
        // Review #15: ListBox-scoped shortcuts need focus inside the
        // ListBox. The dialog must focus RepositoryList on Loaded and
        // ensure a sensible highlight as fallback.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        string? codeBehind = null;

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "RepoDashboard.App",
                "DiscoveryDialog.xaml.cs");

            if (File.Exists(candidate))
            {
                codeBehind = File.ReadAllText(candidate);
                break;
            }

            directory = directory.Parent;
        }

        codeBehind.Should().NotBeNull("could not locate DiscoveryDialog.xaml.cs");
        codeBehind.Should().Contain("RepositoryList.Focus()");
        codeBehind.Should().Contain("SelectedOption");
    }

    [Fact]
    public void KeyboardShortcutsDialog_lists_core_shortcuts()
    {
        // Task 53: the F1 reference must actually document the keymap.
        var text = File.ReadAllText(FindXaml("KeyboardShortcutsDialog.xaml"));

        foreach (var gesture in new[]
                 {
                     "Ctrl+N", "Ctrl+Shift+N", "F2",
                     "Alt+Up", "Alt+Down", "Delete",
                     "F5", "Shift+F5", "F6", "Shift+F6",
                     "Ctrl+F7", "Ctrl+Shift+F7",
                     "Esc", "Enter", "Ctrl+Enter", "Ctrl+Shift+C", "F1"
                 })
        {
            text.Should().Contain(gesture);
        }

        foreach (var group in new[]
                 {
                     "Navigation", "Repository management",
                     "Git operations", "Utilities"
                 })
        {
            text.Should().Contain(group);
        }
    }
}
