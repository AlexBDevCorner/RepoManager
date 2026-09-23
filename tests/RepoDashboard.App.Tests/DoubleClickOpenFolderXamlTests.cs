using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-009: Avalonia view wiring for opening a repository folder by
/// double-clicking its row. Parses AXAML as XML: the gesture must delegate
/// to the existing OpenFolder path, not introduce a second launcher.
/// </summary>
public sealed class DoubleClickOpenFolderXamlTests
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

    private static readonly XNamespace Avalonia =
        "https://github.com/avaloniaui";

    [Fact]
    public void RepositoryGrid_wires_DoubleTapped_to_existing_OpenFolder_path()
    {
        var document = XDocument.Load(FindAxaml("MainWindow.axaml"));

        var grid = document
            .Descendants(Avalonia + "DataGrid")
            .Single();

        grid.Attribute("DoubleTapped")?.Value.Should().Be(
            "OnRepositoryGridDoubleTapped",
            "double-click must translate into the existing OpenFolderCommand path via code-behind");
    }

    [Fact]
    public void DoubleTapped_handler_exists_in_code_behind()
    {
        var method = typeof(MainWindow).GetMethod(
            "OnRepositoryGridDoubleTapped",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull(
            "MainWindow.axaml wires DoubleTapped=\"OnRepositoryGridDoubleTapped\" so the handler must exist");

        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(object));
        parameters[1].ParameterType.FullName.Should().Contain("TappedEventArgs");
    }

    [Fact]
    public void RepositoryGrid_preserves_single_selection_mode()
    {
        // RM-009 must preserve normal single-click selection behavior.
        var document = XDocument.Load(FindAxaml("MainWindow.axaml"));

        var grid = document
            .Descendants(Avalonia + "DataGrid")
            .Single();

        grid.Attribute("SelectionMode")?.Value.Should().Be("Single");
    }

    [Fact]
    public void OpenFolder_toolbar_context_and_enter_actions_still_exist()
    {
        // RM-009 must preserve the existing toolbar, context-menu and Enter
        // shortcut for Open Folder.
        var document = XDocument.Load(FindAxaml("MainWindow.axaml"));
        var text = File.ReadAllText(FindAxaml("MainWindow.axaml"));

        var button = document
            .Descendants(Avalonia + "Button")
            .FirstOrDefault(b =>
                (b.Attribute("Command")?.Value ?? string.Empty).Contains(
                    "OpenFolderCommand", StringComparison.Ordinal));

        button.Should().NotBeNull("the Open Folder toolbar action must be preserved");

        var menuItem = document
            .Descendants(Avalonia + "MenuItem")
            .FirstOrDefault(m =>
                string.Equals(m.Attribute("Header")?.Value, "Open Folder", StringComparison.Ordinal));

        menuItem.Should().NotBeNull("the Open Folder context-menu action must be preserved");
        menuItem!.Attribute("Command")?.Value.Should().Contain("OpenFolderCommand");

        text.Should().Contain("Gesture=\"Enter\"",
            "the Enter keyboard shortcut for Open Folder must be preserved");
        text.Should().Contain("OpenFolderCommand",
            "the Enter binding must still target OpenFolderCommand");
    }
}
