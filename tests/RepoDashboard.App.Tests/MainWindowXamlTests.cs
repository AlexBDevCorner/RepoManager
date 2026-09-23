using System.Xml.Linq;
using FluentAssertions;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-004: Avalonia view regression coverage for <c>MainWindow.axaml</c>.
/// A pure view-model test cannot catch these: sorting happens in the
/// collection view, outside the view model. Keyboard-shortcut contracts are
/// validated by parsing AXAML, not by spinning up UI automation. Mirrors the
/// previous WPF XAML tests but targets Avalonia syntax
/// (<c>Window.KeyBindings</c>, <c>InputGesture</c>, <c>ToolTip.Tip</c>).
/// </summary>
public sealed class MainWindowXamlTests
{
    private static string FindAxaml(string fileName)
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

    private static string FindMainWindowAxaml() => FindAxaml("MainWindow.axaml");

    private static XDocument LoadMainWindow() =>
        XDocument.Load(FindMainWindowAxaml());

    private static readonly XNamespace Avalonia =
        "https://github.com/avaloniaui";

    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Every Window-level KeyBinding, with the command text as written in
    /// AXAML. F1 binds the view-model ShowHelpCommand (Avalonia has no
    /// routed Help command); everything else binds a view-model command.
    /// </summary>
    private static IEnumerable<XElement> WindowKeyBindings(XDocument document) =>
        document.Root
            ?.Element(Avalonia + "Window.KeyBindings")
            ?.Elements(Avalonia + "KeyBinding")
            ?? [];

    [Fact]
    public void MainWindow_DataGrid_disables_user_sorting_and_stays_read_only()
    {
        // Custom order is the dashboard order. An active DataGrid sort would
        // display positions different from the ObservableCollection indexes
        // Move Up/Down persist, silently saving an order the user never saw.
        var document = LoadMainWindow();

        var grid = document
            .Descendants(Avalonia + "DataGrid")
            .Single();

        grid.Attribute("CanUserSortColumns")?.Value.Should().Be("False");
        grid.Attribute("IsReadOnly")?.Value.Should().Be("True");
    }

    [Fact]
    public void MainWindow_repository_grid_is_named_for_initial_focus()
    {
        // MainWindow.axaml.cs focuses RepositoryGrid on Loaded so keyboard
        // navigation works immediately, even while the async load is still
        // in flight.
        var document = LoadMainWindow();

        var grid = document
            .Descendants(Avalonia + "DataGrid")
            .Single();

        grid.Attribute(XamlNamespace + "Name")?.Value.Should().Be("RepositoryGrid");
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
    [InlineData("Ctrl+Shift+B", "CopyBranchCommand")]
    [InlineData("F1", "ShowHelpCommand")]
    public void MainWindow_registers_expected_shortcut(
        string gesture,
        string command)
    {
        // The shortcut layer must call the existing commands directly — no
        // parallel handlers, no Git logic in the view.
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
        // Duplicate gestures would make one shortcut unreachable.
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
        // Expected-set guard — adding, removing or retyping a gesture fails
        // loudly instead of silently changing the keymap.
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
            "Ctrl+Shift+B",
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
        // No CommandParameter on purpose. A null parameter means "use the
        // selection", matching toolbar buttons. Context-menu items keep
        // passing the right-clicked row explicitly.
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
    [InlineData("Copy Branch", "Ctrl+Shift+B")]
    [InlineData("Rename...", "F2")]
    [InlineData("Move Up", "Alt+Up")]
    [InlineData("Move Down", "Alt+Down")]
    [InlineData("Remove from dashboard", "Delete")]
    public void MainWindow_context_menu_reveals_shortcut(
        string header,
        string gesture)
    {
        // InputGesture is display-only in Avalonia; the KeyBindings above
        // remain the functional source.
        var document = LoadMainWindow();

        var item = document
            .Descendants(Avalonia + "MenuItem")
            .FirstOrDefault(m =>
                string.Equals(
                    m.Attribute("Header")?.Value, header,
                    StringComparison.Ordinal));

        item.Should().NotBeNull($"expected context-menu item \"{header}\"");
        item!.Attribute("InputGesture")?.Value.Should().Be(gesture);
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
    [InlineData("CopyBranchCommand", "Ctrl+Shift+B")]
    public void MainWindow_toolbar_tooltip_reveals_shortcut(
        string command,
        string gesture)
    {
        // Mouse users discover shortcuts gradually via tooltips (Avalonia
        // ToolTip.Tip attached property).
        var document = LoadMainWindow();

        var button = document
            .Descendants(Avalonia + "Button")
            .FirstOrDefault(b =>
                (b.Attribute("Command")?.Value ?? string.Empty).Contains(
                    command, StringComparison.Ordinal));

        button.Should().NotBeNull($"expected toolbar button for {command}");
        button!.Attribute("ToolTip.Tip")?.Value.Should().Contain(gesture);
    }

    [Fact]
    public void MainWindow_toolbar_places_copy_branch_next_to_copy_path()
    {
        // RM-010: Copy Branch joins the main toolbar next to Copy Path and
        // reuses the existing CopyBranchCommand (no second clipboard path).
        // Toolbar buttons carry no CommandParameter so a null parameter
        // means "use the selection", preserving the existing enable/disable
        // behavior via CanCopyBranch.
        var document = LoadMainWindow();

        var toolbar = document
            .Descendants(Avalonia + "StackPanel")
            .First(p => string.Equals(
                p.Attribute("DockPanel.Dock")?.Value, "Top",
                StringComparison.Ordinal));

        var buttons = toolbar
            .Elements(Avalonia + "Button")
            .ToList();

        var copyPathIndex = buttons.FindIndex(b =>
            (b.Attribute("Command")?.Value ?? string.Empty).Contains(
                "CopyPathCommand", StringComparison.Ordinal));
        var copyBranchIndex = buttons.FindIndex(b =>
            (b.Attribute("Command")?.Value ?? string.Empty).Contains(
                "CopyBranchCommand", StringComparison.Ordinal));

        copyPathIndex.Should().BeGreaterThanOrEqualTo(0, "expected a Copy Path toolbar button");
        copyBranchIndex.Should().BeGreaterThanOrEqualTo(0, "expected a Copy Branch toolbar button");
        copyBranchIndex.Should().Be(
            copyPathIndex + 1,
            "Copy Branch must sit directly next to Copy Path so related utility actions stay grouped");

        var copyBranch = buttons[copyBranchIndex];
        copyBranch.Attribute("Content")?.Value.Should().Be("Copy Branch");
        copyBranch.Attribute("ToolTip.Tip")?.Value.Should().Contain("Ctrl+Shift+B");
        copyBranch.Attribute("CommandParameter").Should().BeNull(
            "toolbar Copy Branch must use the selection, matching Copy Path");
        copyBranch.Attribute("Margin")?.Value.Should().Be(
            buttons[copyPathIndex].Attribute("Margin")?.Value,
            "Copy Branch must match the style/spacing of the neighboring utility-action buttons");

        // The context-menu action and its shortcut stay untouched.
        var menuItem = document
            .Descendants(Avalonia + "MenuItem")
            .FirstOrDefault(m =>
                string.Equals(m.Attribute("Header")?.Value, "Copy Branch", StringComparison.Ordinal));

        menuItem.Should().NotBeNull("the context-menu Copy Branch action must be preserved");
        menuItem!.Attribute("Command")?.Value.Should().Contain("CopyBranchCommand");
        menuItem.Attribute("InputGesture")?.Value.Should().Be("Ctrl+Shift+B");
    }

    [Fact]
    public void DiscoveryDialog_supports_keyboard_checklist_operations()
    {
        // Arrows move (native ListBox), Space toggles, Ctrl+A selects all
        // available, Ctrl+Shift+A clears. Enter/Esc are covered by
        // IsDefault/IsCancel buttons. The ListBox is named so the dialog can
        // focus it on Loaded — ListBox KeyBindings only work when focus is
        // inside.
        var document = XDocument.Load(FindAxaml("DiscoveryDialog.axaml"));

        var listBox = document
            .Descendants(Avalonia + "ListBox")
            .Single();

        listBox.Attribute(XamlNamespace + "Name")?.Value.Should().Be("RepositoryList");
        listBox.Attribute("SelectedItem")?.Value.Should().Contain("SelectedOption");

        var bindings = listBox
            .Element(Avalonia + "ListBox.KeyBindings")
            ?.Elements(Avalonia + "KeyBinding")
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
            .Descendants(Avalonia + "CheckBox")
            .Single();

        checkBox.Attribute("Focusable")?.Value.Should().Be("False");
    }

    [Fact]
    public void DiscoveryDialog_focuses_checklist_on_open()
    {
        // ListBox-scoped shortcuts need focus inside the ListBox. The dialog
        // must focus RepositoryList on Loaded and ensure a sensible
        // highlight as fallback.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        string? codeBehind = null;

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "RepoDashboard.App",
                "DiscoveryDialog.axaml.cs");

            if (File.Exists(candidate))
            {
                codeBehind = File.ReadAllText(candidate);
                break;
            }

            directory = directory.Parent;
        }

        codeBehind.Should().NotBeNull("could not locate DiscoveryDialog.axaml.cs");
        codeBehind.Should().Contain("RepositoryList.Focus()");
        codeBehind.Should().Contain("SelectedOption");
    }

    [Fact]
    public void KeyboardShortcutsDialog_lists_core_shortcuts()
    {
        // The F1 reference must actually document the keymap.
        var text = File.ReadAllText(FindAxaml("KeyboardShortcutsDialog.axaml"));

        foreach (var gesture in new[]
                 {
                     "Ctrl+N", "Ctrl+Shift+N", "F2",
                     "Alt+Up", "Alt+Down", "Delete",
                     "F5", "Shift+F5", "F6", "Shift+F6",
                     "Ctrl+F7", "Ctrl+Shift+F7",
                      "Esc", "Enter", "Ctrl+Enter", "Ctrl+Shift+C", "Ctrl+Shift+B", "F1"
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
