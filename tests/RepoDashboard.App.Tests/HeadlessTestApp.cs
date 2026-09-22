using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-005: minimal headless application for runtime view tests.
/// Only applies the Fluent theme so controls get proper styling/layout;
/// it deliberately does not run the production <c>App</c> composition root
/// (no DI, no Git, no window showing).
/// </summary>
public sealed class HeadlessTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
