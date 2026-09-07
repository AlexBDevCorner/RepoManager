# Task 48 — Add multiple repository folders at once

- Milestone: 9 — Repository organization
- Type: feature
- Suggested order: 48 → 49 → 50 (this ticket is almost entirely App-layer work)

## Goal

Allow the user to select several Git repository folders in the **Add Repository** dialog and add all selected repositories in one operation.

Example:

Instead of:

1. Click **Add Repository**.
2. Select `C:\source\repos\RepoManager`.
3. Click **Add Repository** again.
4. Select `C:\source\repos\MandarinBotNet`.
5. Repeat.

The user should be able to:

1. Click **Add Repository**.
2. Select several folders.
3. Confirm once.
4. RepoManager adds every valid repository.

This should complement, not replace, the existing **Discover Repositories** functionality (Task 40).

## Current implementation

The current abstraction is:

`src/RepoDashboard.App/Services/IFolderPickerService.cs`

```csharp
public interface IFolderPickerService
{
    string? PickFolder(string title);
}
```

and:

`src/RepoDashboard.App/Services/FolderPickerService.cs`

```csharp
var dialog = new OpenFolderDialog
{
    Title = title,
    Multiselect = false
};
```

`MainWindowViewModel.AddAsync()` then calls `IRepositoryDashboardService.AddAsync()` for exactly one path.

The discovery workflow already demonstrates the desired partial-success model: it can add several discovered repositories by calling `AddAsync()` repeatedly.

Architectural notes:

- `RepositoryConfiguration` already persists `Name` separately from `Path`, so no storage change is needed here.
- WPF's existing `OpenFolderDialog` supports selecting multiple folders and returns them through `FolderNames`, so there is no reason to introduce a third-party picker.

## Design direction

### 1. Do not change the Core dashboard API

Do **not** introduce `AddManyAsync()` into `IRepositoryDashboardService` for this feature.

The existing:

```csharp
Task<RepositoryDashboardItem> AddAsync(
    string path,
    CancellationToken cancellationToken);
```

already owns all important validation:

- directory exists;
- repository isn't already configured;
- directory is actually a Git repository;
- configuration is persisted;
- repository is inspected.

Keep that as the single source of truth.

The multi-add operation is a UI orchestration concern.

### 2. Extend the folder-picker abstraction

Keep the existing single-folder method because repository discovery still needs a root folder.

Add:

```csharp
public interface IFolderPickerService
{
    string? PickFolder(string title);

    IReadOnlyList<string>? PickFolders(string title);
}
```

`null` means the user cancelled.

An empty collection should not normally occur, but the ViewModel should treat it the same as cancellation.

Implementation — update `src/RepoDashboard.App/Services/FolderPickerService.cs` approximately as follows:

```csharp
public IReadOnlyList<string>? PickFolders(string title)
{
    var dialog = new OpenFolderDialog
    {
        Title = title,
        Multiselect = true
    };

    if (dialog.ShowDialog() != true)
    {
        return null;
    }

    return dialog.FolderNames;
}
```

Do not replace `PickFolder()` with this API because **Discover Repositories** intentionally selects one search root.

### 3. Refactor repeated add orchestration

The application now has two multi-add scenarios:

- explicitly selected folders;
- repository discovery results.

Avoid maintaining two subtly different loops.

Extract a private helper inside `src/RepoDashboard.App/ViewModels/MainWindowViewModel.cs`. For example:

```csharp
private async Task<AddRepositoriesSummary> AddRepositoriesAsync(
    IEnumerable<string> paths,
    CancellationToken cancellationToken)
{
    var added = 0;
    var failed = 0;
    RepositoryRowViewModel? lastAdded = null;

    foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var item = await _dashboard.AddAsync(
                path,
                cancellationToken);

            var row = new RepositoryRowViewModel(item);

            Repositories.Add(row);

            lastAdded = row;
            added++;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            failed++;
        }
    }

    if (lastAdded is not null)
    {
        SelectedRepository = lastAdded;
    }

    return new AddRepositoriesSummary(added, failed);
}

private sealed record AddRepositoriesSummary(
    int Added,
    int Failed);
```

The real implementation should also retain useful failure messages rather than blindly swallowing exceptions. For example:

```csharp
private sealed record AddRepositoryFailure(
    string Path,
    string Message);

private sealed record AddRepositoriesSummary(
    int Added,
    IReadOnlyList<AddRepositoryFailure> Failures);
```

This gives the ViewModel enough information for good status text.

### 4. Update AddCommand

Change the existing single-selection code:

```csharp
var path = _folderPicker.PickFolder(
    "Choose a repository folder");
```

to:

```csharp
var paths = _folderPicker.PickFolders(
    "Choose repository folders");

if (paths is null || paths.Count == 0)
{
    return;
}
```

Then start one normal application operation and add all repositories under that operation. Conceptually:

```csharp
var operation = BeginOperation(cancellationToken);
IsBusy = true;

try
{
    var result = await AddRepositoriesAsync(
        paths,
        operation.Token);

    StatusText = result.Failures.Count == 0
        ? $"Added {result.Added} repositories."
        : $"Added {result.Added} of {paths.Count} repositories. " +
          $"{result.Failures.Count} could not be added.";
}
catch (OperationCanceledException)
{
    StatusText = "Adding repositories cancelled.";
}
finally
{
    IsBusy = false;
    EndOperation(operation);
}
```

For exactly one successfully added repository, preserving the existing wording is nice:

```text
Added 'RepoManager'.
```

For multiple repositories:

```text
Added 4 repositories.
```

For partial success:

```text
Added 3 of 5 repositories. 2 could not be added.
```

### 5. Partial success is intentional

Do not implement transaction-like rollback.

Suppose the user selects:

```text
RepoA     valid
RepoB     already added
RepoC     valid
NotGit    ordinary folder
```

Expected result:

```text
RepoA     added
RepoB     skipped/failed
RepoC     added
NotGit    skipped/failed
```

RepoA must not be removed just because NotGit failed.

This matches the existing repository discovery behavior.

### 6. Cancellation semantics

Cancellation should behave consistently with the rest of the application (Tasks 43–44).

If the user selects 20 repositories and cancels after 6 were added:

- the 6 successfully persisted repositories stay added;
- no rollback occurs;
- repositories that have not started must not be processed;
- the UI must not become stuck in `IsBusy=true`.

The existing `BeginOperation()` / `EndOperation()` infrastructure must continue to be used.

### 7. Preserve repository order

Selected repositories should be appended in the order supplied by `OpenFolderDialog.FolderNames`.

Do not alphabetically sort them automatically.

This matters because Task 50 introduces explicit user-defined ordering.

## Files to modify

Primary:

```text
src/RepoDashboard.App/Services/IFolderPickerService.cs
src/RepoDashboard.App/Services/FolderPickerService.cs
src/RepoDashboard.App/ViewModels/MainWindowViewModel.cs
```

Tests:

```text
tests/RepoDashboard.App.Tests/ViewModels/MainWindowViewModelTests.cs
tests/RepoDashboard.App.Tests/ViewModels/MainWindowViewModelHardeningTests.cs
```

No Core or Infrastructure change should be required.

## Required tests

Add tests covering at least:

### Multiple successful repositories

Given three selected folders:

```text
RepoA
RepoB
RepoC
```

Verify:

- `_dashboard.AddAsync()` is invoked three times;
- three rows appear;
- order is RepoA, RepoB, RepoC;
- status reports three added repositories.

### User cancels picker

Picker returns `null`.

Verify:

- dashboard service is never called;
- repository list is unchanged;
- `IsBusy` stays false.

### One repository fails

RepoA succeeds, RepoB fails, RepoC succeeds.

Verify:

- RepoA and RepoC remain visible;
- RepoB is absent;
- operation continues after RepoB;
- status reports partial success.

### Duplicate path in returned selection

If the picker/fake returns the same path twice:

```text
RepoA
RepoA
```

verify it is only attempted once.

### Cancellation

Cancel while processing several paths.

Verify:

- already-added repositories remain;
- later repositories are not started;
- `IsBusy` returns to false.

### Existing Discover functionality

Ensure repository discovery still uses the single-folder picker for its root folder and continues working.

## Acceptance criteria

- [ ] Add Repository allows selecting multiple folders in one Windows dialog.
- [ ] Selecting one folder continues to work.
- [ ] Every valid selected Git repository is added.
- [ ] One invalid/duplicate repository does not prevent other repositories from being added.
- [ ] Duplicate selected paths are not processed twice.
- [ ] User cancellation is handled cleanly.
- [ ] Repositories are added in selection order.
- [ ] Existing Discover Repositories functionality is unaffected.
- [ ] No repository validation logic is duplicated in the App project.
- [ ] Automated tests cover successful, failed, cancelled and mixed batches.
