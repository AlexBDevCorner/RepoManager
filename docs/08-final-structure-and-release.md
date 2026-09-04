# 08 — Final Structure and First Release

## Desired final Core structure (at ~MVP)

```text
RepoDashboard.Core/

    Configuration/
        RepositoryConfiguration.cs
        IRepositoryConfigurationStore.cs

    Git/
        IGitCommandRunner.cs
        GitCommandResult.cs

    Repositories/
        RepositorySnapshot.cs
        RepositoryInspector.cs
        IRepositoryInspector.cs
        Divergence.cs
        DivergenceCalculator.cs
        IDivergenceCalculator.cs

    Sync/
        UpdateEligibility.cs
        UpdateDecision.cs
        UpdateEligibilityClassifier.cs
        IUpdateEligibilityClassifier.cs

        IRepositoryFetcher.cs
        IRepositoryUpdater.cs

        RepositoryOperationResult.cs
        RepositoryUpdateResult.cs

    Dashboard/
        IRepositoryDashboardService.cs
        RepositoryDashboardService.cs
        RepositoryDashboardItem.cs
```

Infrastructure:

```text
RepoDashboard.Infrastructure/

    Git/
        GitCommandRunner.cs
        RepositoryFetcher.cs
        RepositoryUpdater.cs
        GitEnvironment.cs

    Configuration/
        JsonRepositoryConfigurationStore.cs
        JsonApplicationStateStore.cs
```

App:

```text
RepoDashboard.App/

    ViewModels/
        MainWindowViewModel.cs
        RepositoryRowViewModel.cs
        RepositoryDetailsViewModel.cs

    Views/
        MainWindow.xaml
        RepositoryDetailsView.xaml

    Services/
        FolderPickerService.cs

    App.xaml
    App.xaml.cs
```

## Expected first release behaviour

On startup `Repo Dashboard` immediately loads previously configured repositories. It performs a local refresh. No network access is required just to open the application.

Example view:

```text
Store
feature/search
Clean
upstream ↑2 ↓0
vs main ↑5 ↓7
Ahead

Identity
main
Clean
upstream ↑0 ↓3
vs main ↑0 ↓3
Can update

FileStore
main
Dirty
upstream ↑0 ↓1
vs main ↑0 ↓1
Dirty
```

The user clicks `Fetch All`. Repositories update concurrently, with no more than four Git operations at once. The dashboard recalculates.

The user clicks `Update Safe (4)`. The application:

```text
fetches
rechecks state
updates only fast-forwardable branches
skips unsafe repositories
rechecks final state
```

Result:

```text
14 repositories

4 updated
7 already current
1 ahead
1 dirty
1 diverged
```

At no point does the application silently:

```text
change branch
stash work
merge
rebase
reset commits
```

That conservative behaviour is the defining characteristic of the application.
