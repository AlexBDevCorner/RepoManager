# Task 52 — Make repository navigation and dialogs keyboard-first

- Milestone: 10 — Keyboard-first UX
- Type: feature
- Depends on: Task 51
- Suggested order: 51 → 52 → 53

## Goal

Task 51 makes commands callable from the keyboard. This task makes the whole workflow comfortable rather than technically keyboard-accessible. A user should be able to launch RepoManager and continue working without first clicking the repository grid.

The DataGrid is already single-selection and read-only, so standard `Up`, `Down`, `Home`, `End`, `PageUp` and `PageDown` navigation is reused rather than reimplemented.

## Files to modify

```text
src/RepoDashboard.App/MainWindow.xaml
src/RepoDashboard.App/MainWindow.xaml.cs
src/RepoDashboard.App/ViewModels/MainWindowViewModel.cs

src/RepoDashboard.App/DiscoveryDialog.xaml
src/RepoDashboard.App/ViewModels/DiscoveryDialogViewModel.cs

tests/RepoDashboard.App.Tests/ViewModels/MainWindowViewModelTests.cs
tests/RepoDashboard.App.Tests/ViewModels/DiscoveryDialogViewModelTests.cs
```

## 1. Give the repository grid initial keyboard focus

Name the grid `RepositoryGrid` and focus it on `Loaded` via `Dispatcher.BeginInvoke(..., DispatcherPriority.Input)`. Do not put `Keyboard.Focus()` into `MainWindowViewModel`. Focus must work even while the asynchronous repository load is still happening (the window is shown before initialization awaits).

## 2. Establish a useful selection after loading

After repositories have loaded, preserve the previous repository if still present, otherwise select the first row:

```csharp
private void RestoreSelection(Guid? repositoryId)
```

Applied in both `LoadAsync` and `LoadConfigurationRowsAsync`. Result: launch → grid focused → first repository selected → `F5` / `F6` / `Enter` / `F2` immediately useful.

## 3. Keep selection usable after Delete

Capture the removed index before removal; after persistence succeeds select the next row, or the previous row when the last one was deleted. Empty dashboards leave no selection. Moving a repository keeps the same repository selected (existing Task 50 behavior).

## 4. Make repository discovery genuinely keyboard-friendly

The Discovery dialog already supports `Enter` (Add Selected, `IsDefault`) and `Esc` (Cancel, `IsCancel`). Add checklist manipulation:

- `SelectedOption` + `ToggleSelectedCommand` (`Space`, respects `IsSelectable`)
- `SelectAllCommand` (`Ctrl+A`, selectable rows only)
- `ClearSelectionCommand` (`Ctrl+Shift+A`, selectable rows only)

Bind `ListBox.SelectedItem` two-way, add `ListBox.InputBindings`, and set `CheckBox Focusable="False"` so arrows stay on the list. Result:

```text
Up / Down           choose repository
Space               toggle repository
Ctrl+A              select all available
Ctrl+Shift+A        clear selection
Enter               Add Selected
Esc                 cancel
```

## 5. Leave Rename mostly alone

The rename dialog already has Save as `IsDefault`, Cancel as `IsCancel`, focuses the name box and selects the current alias on open. `F2 → type → Enter` already works; don't complicate it.

## Acceptance criteria

- [ ] After startup, a repository is selected and keyboard navigation works immediately.
- [ ] Removing a row selects a sensible neighboring row.
- [ ] Moving a repository keeps the same repository selected.
- [ ] Discovery supports arrow navigation, Space, Select All, Clear All, Enter and Escape.
- [ ] Rename remains usable as `F2 → type → Enter`.
