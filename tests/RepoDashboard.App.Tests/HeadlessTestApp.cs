using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-005: minimal headless application for runtime view tests.
/// Only applies the Fluent theme so controls get proper styling/layout;
/// it deliberately does not run the production <c>App</c> composition root
/// (no DI, no Git, no window showing).
/// RM-007: mirrors production <c>App.axaml</c> by also registering the
/// Avalonia DataGrid Fluent theme. Without it the repository
/// <c>DataGrid</c> has no row/cell templates and renders blank even though
/// <c>MainWindowViewModel.Repositories</c> is populated.
/// </summary>
public sealed class HeadlessTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://RepoDashboard.App.Tests/App.axaml"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
