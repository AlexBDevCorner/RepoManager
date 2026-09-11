# Task 51 — Add application keyboard shortcuts

- Milestone: 10 — Keyboard-first UX
- Type: feature
- Depends on: Task 50
- Suggested order: 51 → 52 → 53

## Goal

Allow nearly every action currently available from the toolbar or repository context menu to be executed directly from the keyboard.

The current `MainWindow.xaml` already binds toolbar buttons and context-menu items to `AddCommand`, `RefreshCommand`, `FetchCommand`, `UpdateCommand`, `RenameCommand`, `MoveUpCommand`, etc. The keyboard layer must call those same commands rather than creating parallel event handlers.

## Architecture decision

Do **not** add things such as:

```csharp
private void HandleRefreshShortcut(...)
private void HandleFetchShortcut(...)
private void HandleUpdateShortcut(...)
```

and do not call `_dashboard` from `MainWindow.xaml.cs`.

The flow should remain:

```text
Keyboard gesture
    ↓
existing ICommand
    ↓
MainWindowViewModel
    ↓
existing dashboard/service logic
```

This is particularly important because the existing commands already contain the correct `CanExecute` rules around `IsBusy`, Git availability, selected repositories, cancellation and configuration-only actions.

## Files to modify

```text
src/RepoDashboard.App/MainWindow.xaml

tests/RepoDashboard.App.Tests/MainWindowXamlTests.cs
```

No change should be required in:

```text
RepoDashboard.Core
RepoDashboard.Infrastructure
RepositoryDashboardService
```

## Implementation

Add `Window.InputBindings` near the beginning of `MainWindow.xaml` (no `CommandParameter` — a null parameter means "use the selection", matching toolbar buttons):

| Gesture | Command |
| --- | --- |
| `Ctrl+N` | `AddCommand` |
| `Ctrl+Shift+N` | `DiscoverCommand` |
| `F2` | `RenameCommand` |
| `Alt+Up` | `MoveUpCommand` |
| `Alt+Down` | `MoveDownCommand` |
| `Delete` | `RemoveCommand` |
| `F5` | `RefreshCommand` |
| `Shift+F5` | `RefreshAllCommand` |
| `F6` | `FetchCommand` |
| `Shift+F6` | `FetchAllCommand` |
| `Ctrl+F7` | `UpdateCommand` |
| `Ctrl+Shift+F7` | `UpdateAllCommand` |
| `Escape` | `CancelCommand` |
| `Enter` | `OpenFolderCommand` |
| `Ctrl+Enter` | `OpenTerminalCommand` |
| `Ctrl+Shift+C` | `CopyPathCommand` |

Do not use bare `F7` for Update. Refresh and Fetch don't change the checked-out branch; Update can. Making the mutating operation require `Ctrl` is a deliberate safety distinction. `Alt+Up` / `Alt+Down` are preserved from Task 50.

## Important safety behavior

`Delete` must invoke the existing `RemoveCommand` with its confirmation dialog. `Ctrl+F7` / `Ctrl+Shift+F7` must go through the existing safe-update commands — a shortcut must never create a second code path around fast-forward safety rules. `Esc` binds to `CancelCommand`; when nothing is running `CanCancel()` already returns false, so `Esc` is harmless.

## Required tests

In `MainWindowXamlTests.cs`: theory asserting each `Gesture` → `Command` mapping exists in `Window.InputBindings`, plus a regression test ensuring no duplicate gestures.

## Acceptance criteria

- [ ] All mapped actions work without the mouse.
- [ ] Disabled toolbar actions are also disabled through the shortcut (no `CanExecute` bypass).
- [ ] Delete keeps the existing confirmation.
- [ ] Update retains all existing safety rules.
- [ ] Shortcut handling contains no Git/business logic.
