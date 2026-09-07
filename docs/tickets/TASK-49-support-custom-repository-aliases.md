# Task 49 — Support custom repository aliases

- Milestone: 9 — Repository organization
- Type: feature
- Suggested order: 48 → 49 → 50 (this ticket establishes the pattern for configuration-only mutations)

## Goal

Allow the user to assign a custom display name to a configured repository.

Example:

Physical folder:

```text
C:\source\repos\StandardsDigital.Store.Web
```

Current display:

```text
StandardsDigital.Store.Web
```

Desired alias:

```text
Store
```

Another example:

```text
C:\source\repos\MandarinBotNet
```

may be displayed as:

```text
Fantasy Bot
```

The alias affects only RepoManager presentation. It must never rename the physical folder or Git repository.

## Important architecture decision

Do **not** add a second property such as:

```csharp
Alias
DisplayName
CustomName
```

`RepositoryConfiguration` already contains:

```csharp
public required string Name { get; init; }

public required string Path { get; init; }
```

Use `Name` as the user-configurable display name.

Its semantics become:

> The persisted display name of the repository. Defaults to the physical folder name when the repository is added, but may later be changed by the user.

This avoids unnecessary model and JSON migration work.

## 1. Preserve current add behavior

New repositories should still initially use the physical folder name (Task 21):

```csharp
Name = new DirectoryInfo(fullPath).Name
```

No alias dialog should be forced during Add Repository.

The user can rename the repository afterwards.

This keeps adding repositories fast and makes aliases optional.

## 2. Add a dashboard-service rename operation

Extend `src/RepoDashboard.Core/Dashboard/IRepositoryDashboardService.cs` with:

```csharp
Task<RepositoryConfiguration> RenameAsync(
    Guid repositoryId,
    string name,
    CancellationToken cancellationToken);
```

The service rather than the ViewModel must own configuration mutation.

## 3. Implement RenameAsync

In `src/RepoDashboard.Core/Dashboard/RepositoryDashboardService.cs` the implementation should:

1. validate the proposed name;
2. load configurations;
3. find the repository by ID;
4. replace only its `Name`;
5. preserve its position in the collection;
6. save configuration;
7. return the updated configuration.

Example:

```csharp
public async Task<RepositoryConfiguration> RenameAsync(
    Guid repositoryId,
    string name,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException(
            "Repository name must not be empty.",
            nameof(name));
    }

    var normalizedName = name.Trim();

    var configurations =
        (await _store.LoadAsync(cancellationToken)).ToList();

    var index = configurations.FindIndex(
        c => c.Id == repositoryId);

    if (index < 0)
    {
        throw new KeyNotFoundException(
            $"Repository '{repositoryId}' is not on the dashboard.");
    }

    var updated = configurations[index] with
    {
        Name = normalizedName
    };

    configurations[index] = updated;

    await _store.SaveAsync(
        configurations,
        cancellationToken);

    return updated;
}
```

Do not inspect Git.

Renaming is configuration-only.

## 4. Alias uniqueness

Do **not** require names to be unique.

These should both be legal:

```text
Store
Store
```

Repository identity is already represented by:

```csharp
Guid Id
```

and the actual repository location by:

```csharp
Path
```

Forcing alias uniqueness creates unnecessary restrictions.

## 5. Add a small rename dialog abstraction

Do not put WPF dialog creation inside `MainWindowViewModel`.

Introduce something such as:

```text
src/RepoDashboard.App/Services/IRepositoryNameDialogService.cs
src/RepoDashboard.App/Services/RepositoryNameDialogService.cs
src/RepoDashboard.App/RenameRepositoryDialog.xaml
src/RepoDashboard.App/RenameRepositoryDialog.xaml.cs
```

Interface:

```csharp
public interface IRepositoryNameDialogService
{
    string? RequestName(
        string currentName,
        string repositoryPath);
}
```

`null` means Cancel.

The dialog should contain:

```text
Repository name

[ Store                         ]

Path:
C:\source\repos\StandardsDigital.Store.Web

                 Cancel   Save
```

The current name should be selected when the dialog opens so typing immediately replaces it.

## 6. Register the service through DI

Update `src/RepoDashboard.App/App.xaml.cs` following the same pattern as the folder picker and discovery dialog services.

Do not instantiate the dialog service directly inside the ViewModel.

## 7. Add RenameCommand

Add a command to `MainWindowViewModel`.

Conceptually:

```csharp
private bool CanRename(RepositoryRowViewModel? target) =>
    !IsBusy &&
    (target ?? SelectedRepository) is not null;

[RelayCommand(CanExecute = nameof(CanRename))]
private async Task RenameAsync(
    RepositoryRowViewModel? target,
    CancellationToken cancellationToken)
{
    var selected = target ?? SelectedRepository;

    if (selected is null)
    {
        return;
    }

    var name = _repositoryNameDialog.RequestName(
        selected.Name,
        selected.DetailsPath);

    if (name is null)
    {
        return;
    }

    try
    {
        var updated = await _dashboard.RenameAsync(
            selected.RepositoryId,
            name,
            cancellationToken);

        selected.SetName(updated.Name);

        StatusText = $"Renamed repository to '{updated.Name}'.";
    }
    catch (Exception ex)
    {
        StatusText =
            $"Could not rename '{selected.Name}': {ex.Message}";
    }
}
```

Because this operation touches configuration only, it should remain available even if Git is unavailable.

Follow the same principle currently used for Remove.

## 8. Make RepositoryRowViewModel explicit

Although `Name` is already observable, expose an intent-specific method instead of letting arbitrary code manipulate presentation state. For example:

```csharp
public void SetName(string name)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);

    Name = name;
}
```

This makes ViewModel code clearer.

A later Refresh will receive the renamed configuration from the dashboard service and continue displaying the same alias.

## 9. Add UI actions

Update `src/RepoDashboard.App/MainWindow.xaml`.

Add a toolbar action:

```text
Rename
```

and a row context-menu item:

```text
Rename...
```

Recommended context menu:

```text
Refresh
Fetch
Update
----------------
Open Folder
Open Terminal
Copy Path
----------------
Rename...
Move Up
Move Down
----------------
Remove from dashboard
```

Move actions will be implemented by Task 50.

## 10. Do not make the DataGrid generally editable

The current DataGrid is:

```xml
IsReadOnly="True"
```

Keep it that way.

Do not enable direct arbitrary cell editing simply to support repository names.

A dedicated Rename action:

- is more explicit;
- is easier to validate;
- makes persistence failure handling straightforward;
- doesn't accidentally make other columns editable;
- works naturally from the existing row context menu.

## 11. Persistence compatibility

No `repositories.json` schema migration should be necessary.

Existing:

```json
{
  "id": "...",
  "name": "RepoManager",
  "path": "C:\\source\\repos\\RepoManager"
}
```

After rename:

```json
{
  "id": "...",
  "name": "My Repo Manager",
  "path": "C:\\source\\repos\\RepoManager"
}
```

`Path` remains untouched.

## Files to modify

Core:

```text
src/RepoDashboard.Core/Dashboard/IRepositoryDashboardService.cs
src/RepoDashboard.Core/Dashboard/RepositoryDashboardService.cs
```

App:

```text
src/RepoDashboard.App/App.xaml.cs
src/RepoDashboard.App/MainWindow.xaml
src/RepoDashboard.App/ViewModels/MainWindowViewModel.cs
src/RepoDashboard.App/ViewModels/RepositoryRowViewModel.cs
```

New App files:

```text
src/RepoDashboard.App/Services/IRepositoryNameDialogService.cs
src/RepoDashboard.App/Services/RepositoryNameDialogService.cs
src/RepoDashboard.App/RenameRepositoryDialog.xaml
src/RepoDashboard.App/RenameRepositoryDialog.xaml.cs
```

Tests:

```text
tests/RepoDashboard.Core.Tests/Dashboard/RepositoryDashboardServiceTests.cs
tests/RepoDashboard.App.Tests/ViewModels/MainWindowViewModelTests.cs
tests/RepoDashboard.App.Tests/ViewModels/RepositoryRowViewModelTests.cs
tests/RepoDashboard.IntegrationTests/Configuration/JsonRepositoryConfigurationStoreTests.cs
```

## Required tests

### Core service

#### Rename repository

Verify:

```text
Name -> changed
Path -> unchanged
Id -> unchanged
PreferredRemote -> unchanged
Enabled -> unchanged
position in list -> unchanged
```

#### Unknown repository

Verify:

```csharp
KeyNotFoundException
```

#### Empty name

Test:

```text
""
" "
"\t"
```

and verify configuration is not changed.

#### Whitespace trimming

Input:

```text
"   Store   "
```

stored value:

```text
"Store"
```

#### Duplicate aliases

Two different repositories can both be renamed to:

```text
Store
```

### App ViewModel

Verify:

- Rename command uses selected row;
- context-menu target overrides selected row;
- Cancel leaves repository unchanged;
- successful rename updates displayed name immediately;
- persistence failure leaves displayed name unchanged;
- Rename is disabled while another operation is running.

### Persistence

Save:

```text
Name = "Store"
Path = C:\source\repos\StandardsDigital.Store.Web
```

load it again and verify the alias survives restart.

## Acceptance criteria

- [ ] A repository can be renamed from the toolbar.
- [ ] A repository can be renamed from its context menu.
- [ ] The physical directory is never renamed.
- [ ] Git configuration/remotes are never modified.
- [ ] The alias persists after application restart.
- [ ] Existing repositories continue using their current names until renamed.
- [ ] Newly added repositories initially use the folder name.
- [ ] Duplicate aliases are allowed.
- [ ] Empty aliases are rejected.
- [ ] Rename does not invoke Git inspection/fetch/update.
- [ ] The DataGrid remains read-only.
- [ ] Automated tests cover persistence, validation and UI behavior.
