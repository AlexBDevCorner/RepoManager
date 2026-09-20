using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Discovery;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Lifetime;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.State;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Configuration;
using RepoDashboard.Infrastructure.Git;
using RepoDashboard.Infrastructure.State;

namespace RepoDashboard.App;

/// <summary>
/// Avalonia application composition root (RM-004). All services are wired
/// here; view models and views receive dependencies via constructor
/// injection. Preserves the existing dependency direction:
/// <c>Avalonia App -&gt; Core + Infrastructure</c>.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IApplicationShutdown, ApplicationShutdown>();
                services.AddSingleton<IGitCommandRunner, GitCommandRunner>();
                services.AddSingleton<IGitEnvironment, GitEnvironment>();
                services.AddSingleton<IRepositoryConfigurationStore, JsonRepositoryConfigurationStore>();
                services.AddSingleton<IOperationStateStore, JsonOperationStateStore>();
                services.AddSingleton<IDivergenceCalculator, DivergenceCalculator>();
                services.AddSingleton<IRepositoryInspector, RepositoryInspector>();
                services.AddSingleton<IRepositoryFetcher, RepositoryFetcher>();
                services.AddSingleton<IRepositoryUpdater, RepositoryUpdater>();
                services.AddSingleton<IUpdateEligibilityClassifier, UpdateEligibilityClassifier>();
                services.AddSingleton<IRepositoryDashboardService, RepositoryDashboardService>();
                services.AddSingleton<IRepositoryDiscoveryService, RepositoryDiscoveryService>();
                services.AddSingleton<IFolderPickerService, FolderPickerService>();
                services.AddSingleton<IDiscoveryDialogService, DiscoveryDialogService>();
                services.AddSingleton<IRepositoryNameDialogService, RepositoryNameDialogService>();
                services.AddSingleton<IRepositoryRemovalConfirmationService, RepositoryRemovalConfirmationService>();
                services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
                services.AddSingleton<IFolderLauncher, DesktopFolderLauncher>();
                services.AddSingleton<ITerminalLauncher, TerminalLauncher>();
                services.AddSingleton<IKeyboardShortcutsDialogService, KeyboardShortcutsDialogService>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    _host.Services.GetService<MainWindowViewModel>()?.NotifyShuttingDown();
                }
                catch (ObjectDisposedException)
                {
                }
            };

            desktop.Exit += (_, _) =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    _host.StopAsync(timeout.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }

                _host.Dispose();
            };

            // Show the window before inspecting repositories: inspection runs
            // multiple Git commands per repository, so awaiting it first would
            // look like the application did not launch at all.
            mainWindow.Show();

            await viewModel.InitializeAsync();

            if (!viewModel.IsGitAvailable)
            {
                await ShowGitNotFoundAsync(mainWindow, viewModel.GitStatusText);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowGitNotFoundAsync(Window owner, string message)
    {
        var dialog = new Window
        {
            Title = "RepoDashboard — Git not found",
            Width = 440,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 8)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 16, 16)
        };

        okButton.Click += (_, _) => dialog.Close();

        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(okButton);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
    }
}
