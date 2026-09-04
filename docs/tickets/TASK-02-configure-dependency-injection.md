# Task 2 — Configure dependency injection

- Milestone: 1 — Git foundation
- Type: tech-setup

## Goal

Have one clearly defined composition root. Do not instantiate services randomly throughout the application.

Bad:

```csharp
var runner = new GitCommandRunner();
```

inside a ViewModel.

Good:

```csharp
public MainWindowViewModel(
    IRepositoryDashboardService repositoryDashboard)
```

## Implementation — App.xaml.cs

```csharp
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(
        StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IGitCommandRunner, GitCommandRunner>();

                services.AddSingleton<IRepositoryConfigurationStore,
                    JsonRepositoryConfigurationStore>();

                services.AddSingleton<IRepositoryInspector,
                    RepositoryInspector>();

                services.AddSingleton<IRepositoryFetcher,
                    RepositoryFetcher>();

                services.AddSingleton<IRepositoryUpdater,
                    RepositoryUpdater>();

                services.AddSingleton<IUpdateEligibilityClassifier,
                    UpdateEligibilityClassifier>();

                services.AddSingleton<IRepositoryDashboardService,
                    RepositoryDashboardService>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

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
```

## Acceptance criteria

- [ ] `MainWindowViewModel` is resolved through dependency injection.
- [ ] No `new GitCommandRunner()` (or other services) inside ViewModels.
