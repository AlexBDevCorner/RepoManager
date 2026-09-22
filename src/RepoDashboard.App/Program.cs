using Avalonia;

namespace RepoDashboard.App;

/// <summary>
/// Avalonia entry point (RM-004). Builds the Avalonia application with
/// platform detection so the same binary runs on Windows and Linux.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
