using System.Xml.Linq;
using FluentAssertions;

namespace RepoDashboard.App.Tests;

/// <summary>
/// Task 50 regression coverage for <c>MainWindow.xaml</c> itself.
/// A pure view-model test cannot catch these: WPF sorting happens in
/// the collection view, outside the view model.
/// </summary>
public sealed class MainWindowXamlTests
{
    private static string FindMainWindowXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "RepoDashboard.App",
                "MainWindow.xaml");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate src/RepoDashboard.App/MainWindow.xaml from "
            + AppContext.BaseDirectory);
    }

    [Fact]
    public void MainWindow_DataGrid_disables_user_sorting_and_stays_read_only()
    {
        // Task 50: custom order is the dashboard order. An active DataGrid
        // sort would display positions different from the
        // ObservableCollection indexes Move Up/Down persist, silently
        // saving an order the user never saw.
        var document = XDocument.Load(FindMainWindowXaml());
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var grid = document
            .Descendants(presentation + "DataGrid")
            .Single();

        grid.Attribute("CanUserSortColumns")?.Value.Should().Be("False");
        grid.Attribute("IsReadOnly")?.Value.Should().Be("True");
    }
}
