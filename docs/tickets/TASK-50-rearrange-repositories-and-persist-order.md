# Task 50 — Allow repositories to be rearranged and persist custom order

- Milestone: 9 — Repository organization
- Type: feature
- Suggested order: 48 → 49 → 50 (this ticket reuses the configuration-only mutation pattern from Task 49)
- Depends on: Task 48 (multi-add appends in selection order)

## Goal

Allow the user to define the order in which repositories appear in RepoManager.

Example initial order:

```text
RepoManager
MandarinBotNet
Store
Search
Identity
```

User changes it to:

```text
Store
Search
Identity
RepoManager
MandarinBotNet
```

After restarting RepoManager, the same order must remain.

## Important architecture decision

Do **not** introduce:

```csharp
int Order
int SortOrder
int Position
```

into `RepositoryConfiguration`.

The configuration store already persists repositories as an ordered JSON array. For example:

```json
{
  "repositories": [
    { "name": "Store", "...": "..." },
    { "name": "Search", "...": "..." },
    { "name": "Identity", "...": "..." }
  ]
}
```

JSON array order is deterministic.

Therefore:

> The position of a repository inside `repositories.json` is its persisted dashboard order.

This avoids:

- schema migration;
- duplicate sort indexes;
- gaps;
- normalization logic;
- conflicting indexes;
- additional model state.

## 1. Add a generic move operation to the dashboard service

Extend `src/RepoDashboard.Core/Dashboard/IRepositoryDashboardService.cs` with:

```csharp
Task MoveAsync(
    Guid repositoryId,
    int newIndex,
    CancellationToken cancellationToken);
```

Do not create separate Core methods such as:

```csharp
MoveUpAsync()
MoveDownAsync()
```

"Up" and "Down" are UI concepts.

Core only needs:

> move repository X to position Y.

This also leaves the API ready for future drag-and-drop support.

## 2. Implement MoveAsync

In `src/RepoDashboard.Core/Dashboard/RepositoryDashboardService.cs` use the order returned by the configuration store.

Example:

```csharp
public async Task MoveAsync(
    Guid repositoryId,
    int newIndex,
    CancellationToken cancellationToken)
{
    var configurations =
        (await _store.LoadAsync(cancellationToken)).ToList();

    var currentIndex = configurations.FindIndex(
        c => c.Id == repositoryId);

    if (currentIndex < 0)
    {
        throw new KeyNotFoundException(
            $"Repository '{repositoryId}' is not on the dashboard.");
    }

    if (newIndex < 0 || newIndex >= configurations.Count)
    {
        throw new ArgumentOutOfRangeException(
            nameof(newIndex));
    }

    if (currentIndex == newIndex)
    {
        return;
    }

    var configuration = configurations[currentIndex];

    configurations.RemoveAt(currentIndex);
    configurations.Insert(newIndex, configuration);

    await _store.SaveAsync(
        configurations,
        cancellationToken);
}
```

This operation must not:

- inspect Git;
- fetch;
- update branches;
- change repository IDs;
- modify operational state.

It modifies only configuration ordering.

## 3. Add Move Up / Move Down commands

For the first implementation, use explicit commands rather than drag-and-drop.

Add:

```text
Move Up
Move Down
```

Advantages:

- obvious behavior;
- keyboard-friendly;
- simple MVVM implementation;
- easy automated testing;
- no WPF drag/drop event plumbing;
- service API remains reusable if drag/drop is added later.

Drag-and-drop should be considered a future UX enhancement, not required for this ticket.

## 4. Command availability

Add to `MainWindowViewModel`:

```csharp
private bool CanMoveUp(RepositoryRowViewModel? target)
{
    if (IsBusy)
    {
        return false;
    }

    var row = target ?? SelectedRepository;

    if (row is null)
    {
        return false;
    }

    return Repositories.IndexOf(row) > 0;
}
```

and:

```csharp
private bool CanMoveDown(RepositoryRowViewModel? target)
{
    if (IsBusy)
    {
        return false;
    }

    var row = target ?? SelectedRepository;

    if (row is null)
    {
        return false;
    }

    var index = Repositories.IndexOf(row);

    return index >= 0 &&
           index < Repositories.Count - 1;
}
```

Like Remove and Rename, ordering is configuration-only.

It should therefore continue working when Git is unavailable.

## 5. Persist first, update UI second

Do not move the `ObservableCollection` before persistence succeeds.

Correct flow:

```text
calculate new index
    ↓
_dashboard.MoveAsync(...)
    ↓
save succeeds
    ↓
Repositories.Move(...)
```

Example:

```csharp
[RelayCommand(CanExecute = nameof(CanMoveUp))]
private async Task MoveUpAsync(
    RepositoryRowViewModel? target,
    CancellationToken cancellationToken)
{
    var row = target ?? SelectedRepository;

    if (row is null)
    {
        return;
    }

    var currentIndex = Repositories.IndexOf(row);

    if (currentIndex <= 0)
    {
        return;
    }

    var newIndex = currentIndex - 1;

    try
    {
        await _dashboard.MoveAsync(
            row.RepositoryId,
            newIndex,
            cancellationToken);

        Repositories.Move(
            currentIndex,
            newIndex);

        SelectedRepository = row;

        StatusText = $"Moved '{row.Name}' up.";
    }
    catch (Exception ex)
    {
        StatusText =
            $"Could not move '{row.Name}': {ex.Message}";
    }
}
```

MoveDown follows the same pattern.

If persistence fails, the visible order remains unchanged.

## 6. Preserve selection

After moving:

```text
Store
Search  ← selected
Identity
```

to:

```text
Search  ← still selected
Store
Identity
```

`SelectedRepository` must continue referencing the same row object.

`ObservableCollection.Move()` naturally allows this.

## 7. Update command notifications

Move command availability depends on both:

- current selection;
- position of that selection.

After:

- load;
- add;
- remove;
- move;
- selection change;

refresh both command states.

A helper is preferable:

```csharp
private void NotifyOrderingCommands()
{
    MoveUpCommand.NotifyCanExecuteChanged();
    MoveDownCommand.NotifyCanExecuteChanged();
}
```

Call it after collection changes.

Also include the commands in the appropriate CommunityToolkit:

```csharp
[NotifyCanExecuteChangedFor(...)]
```

attributes on `SelectedRepository` and `IsBusy`.

## 8. UI

Update `src/RepoDashboard.App/MainWindow.xaml`.

Add buttons near repository management actions:

```text
+ Add Repository
Discover Repositories...
Rename
↑
↓
Remove from dashboard
```

The buttons may use text initially:

```text
Move Up
Move Down
```

which is clearer than icons alone.

Add the same actions to the row context menu:

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

Context-menu actions must operate on the row that was right-clicked, following the existing command-parameter approach.

## 9. Optional keyboard shortcuts

Recommended but not required for completion:

```text
Alt+Up      Move repository up
Alt+Down    Move repository down
```

These can invoke the same commands and should not contain separate ordering logic.

## 10. New repositories

Newly added repositories must continue to append to the end.

Current service behavior already saves using:

```csharp
[.. configurations, configuration]
```

Keep this behavior.

Example:

Existing:

```text
Store
Search
Identity
```

Add RepoManager:

```text
Store
Search
Identity
RepoManager
```

The user can then manually reposition it.

Task 48 multi-add should similarly append repositories in selection order.

## 11. Removing repositories

Removal should preserve relative order.

Example:

```text
A
B
C
D
```

remove `B`:

```text
A
C
D
```

No order normalization is needed.

## 12. Batch operations must not reorder rows

The following operations must preserve user-defined order:

```text
Refresh
Refresh All
Fetch
Fetch All
Update
Update Safe Repositories
```

They update repository state; they must never sort the dashboard based on:

- name;
- branch;
- update status;
- fetch result;
- completion order.

This is particularly important for concurrent batch operations: faster repositories must not jump above slower repositories.

The identity used for row updates remains `RepositoryConfiguration.Id`.

## 13. Do not add automatic DataGrid sorting

Once custom order exists, clicking a DataGrid header and leaving it sorted could create confusion between:

```text
persisted custom order
```

and:

```text
temporary UI sort order
```

For this ticket, custom ordering should be the dashboard order.

Do not introduce automatic alphabetical sorting.

If sortable columns are desired later, treat temporary sorting as a separate feature with a clear "Custom order" mode.

## Files to modify

Core:

```text
src/RepoDashboard.Core/Dashboard/IRepositoryDashboardService.cs
src/RepoDashboard.Core/Dashboard/RepositoryDashboardService.cs
```

App:

```text
src/RepoDashboard.App/MainWindow.xaml
src/RepoDashboard.App/ViewModels/MainWindowViewModel.cs
```

Tests:

```text
tests/RepoDashboard.Core.Tests/Dashboard/RepositoryDashboardServiceTests.cs
tests/RepoDashboard.App.Tests/ViewModels/MainWindowViewModelTests.cs
tests/RepoDashboard.App.Tests/ViewModels/MainWindowViewModelHardeningTests.cs
tests/RepoDashboard.IntegrationTests/Configuration/JsonRepositoryConfigurationStoreTests.cs
```

No change should be necessary to:

```text
RepositoryConfiguration.cs
```

## Required tests

### Core — move upward

Starting configuration:

```text
A
B
C
```

Move C to index 0.

Persisted configuration must become:

```text
C
A
B
```

### Core — move downward

Starting:

```text
A
B
C
```

Move A to index 2.

Result:

```text
B
C
A
```

### Core — same position

Move B from index 1 to index 1.

Verify:

- succeeds;
- order remains unchanged.

### Core — invalid ID

Verify:

```csharp
KeyNotFoundException
```

### Core — invalid destination

Test:

```text
-1
Count
Count + 1
```

Verify configuration remains unchanged.

### Persistence round-trip

Save:

```text
C
A
B
```

Create/load through the JSON configuration store.

Verify the loaded order remains:

```text
C
A
B
```

### App — Move Up

Starting:

```text
A
B
C
```

select B and execute Move Up.

Verify:

```text
B
A
C
```

and B remains selected.

### App — Move Down

Starting:

```text
A
B
C
```

select B and execute Move Down.

Verify:

```text
A
C
B
```

### Boundary behavior

For the first repository:

```text
Move Up disabled
Move Down enabled
```

For the last repository:

```text
Move Up enabled
Move Down disabled
```

For only one repository:

```text
Move Up disabled
Move Down disabled
```

### Persistence failure

Make `_dashboard.MoveAsync()` fail.

Verify:

- ObservableCollection is not moved;
- selection stays unchanged;
- status reports the error.

### Batch refresh ordering

Start:

```text
Store
Search
RepoManager
```

run Refresh All / Fetch All and return results in a different completion order.

The displayed order must remain:

```text
Store
Search
RepoManager
```

## Acceptance criteria

- [ ] User can move a repository up.
- [ ] User can move a repository down.
- [ ] First repository cannot move further up.
- [ ] Last repository cannot move further down.
- [ ] Ordering persists after restarting RepoManager.
- [ ] No new order/index field is added to RepositoryConfiguration.
- [ ] `repositories.json` array order is the source of truth.
- [ ] Add Repository appends to the end.
- [ ] Multi-add appends repositories in selection order.
- [ ] Remove preserves relative order.
- [ ] Refresh/fetch/update operations do not alter order.
- [ ] A failed persistence operation does not change visible order.
- [ ] Context-menu actions target the right-clicked row.
- [ ] Automated tests cover service, persistence and ViewModel behavior.
