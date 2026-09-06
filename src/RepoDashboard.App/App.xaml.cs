using System.Windows;
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
/// Application composition root. All services are wired here;
/// ViewModels and views receive dependencies via constructor injection.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(
        StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Task 41 — structured logging via Microsoft.Extensions.Logging.
                // The default builder already adds console/debug/event-source;
                // keep Information as the floor so fetch/update start/completed
                // lines are visible without verbose Git Debug noise.
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                // Git and application services (Tasks 3+) are registered here
                // as they are implemented, e.g.:
                // Shared shutdown signal: user Cancel vs shutdown stay
                // distinct downstream — post-commit work observes only this.
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

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var viewModel = _host.Services
            .GetRequiredService<MainWindowViewModel>();

        // Show the window before inspecting repositories: inspection runs
        // multiple Git commands per repository, so awaiting it first would
        // look like the application did not launch at all.
        _host.Services
            .GetRequiredService<MainWindow>()
            .Show();

        await viewModel.InitializeAsync();

        if (!viewModel.IsGitAvailable)
        {
            MessageBox.Show(
                viewModel.GitStatusText,
                "RepoDashboard — Git not found",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        base.OnStartup(e);
    }

    /// <summary>
    /// Clean shutdown (Task 44): cancel in-flight Git work first so the
    /// command runner kills git.exe process trees instead of orphaning
    /// them, then drain the host briefly. No new operations may start
    /// once the view model is notified.
    /// </summary>
    protected override async void OnExit(
        ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                _host.Services
                    .GetService<MainWindowViewModel>()
                    ?.NotifyShuttingDown();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                await _host.StopAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // Shutdown budget elapsed — process exit cleans up.
            }

            _host.Dispose();
        }

        base.OnExit(e);
    }
}
