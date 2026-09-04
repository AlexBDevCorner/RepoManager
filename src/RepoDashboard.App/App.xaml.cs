using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Configuration;
using RepoDashboard.Infrastructure.Git;

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
            .ConfigureServices(services =>
            {
                // Git and application services (Tasks 3+) are registered here
                // as they are implemented, e.g.:
                services.AddSingleton<IGitCommandRunner, GitCommandRunner>();
                services.AddSingleton<IGitEnvironment, GitEnvironment>();
                services.AddSingleton<IRepositoryConfigurationStore, JsonRepositoryConfigurationStore>();
                services.AddSingleton<IDivergenceCalculator, DivergenceCalculator>();
                services.AddSingleton<IRepositoryInspector, RepositoryInspector>();
                services.AddSingleton<IUpdateEligibilityClassifier, UpdateEligibilityClassifier>();
                // services.AddSingleton<IRepositoryFetcher, RepositoryFetcher>();
                // services.AddSingleton<IRepositoryUpdater, RepositoryUpdater>();
                // services.AddSingleton<IRepositoryDashboardService, RepositoryDashboardService>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var viewModel = _host.Services
            .GetRequiredService<MainWindowViewModel>();

        await viewModel.InitializeAsync();

        if (!viewModel.IsGitAvailable)
        {
            MessageBox.Show(
                viewModel.GitStatusText,
                "RepoDashboard — Git not found",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        _host.Services
            .GetRequiredService<MainWindow>()
            .Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(
        ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
