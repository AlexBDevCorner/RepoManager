# Task 53 — Make shortcuts discoverable and protect the keymap with tests

- Milestone: 10 — Keyboard-first UX
- Type: feature
- Depends on: Task 52
- Suggested order: 51 → 52 → 53

## Goal

Users should be able to learn the important shortcuts from the UI itself.

## Files to add

```text
src/RepoDashboard.App/KeyboardShortcutsDialog.xaml
src/RepoDashboard.App/KeyboardShortcutsDialog.xaml.cs
```

## Files to modify

```text
src/RepoDashboard.App/MainWindow.xaml
src/RepoDashboard.App/MainWindow.xaml.cs

tests/RepoDashboard.App.Tests/MainWindowXamlTests.cs

README.md
docs/README.md
```

## 1. Show shortcuts in context menus

WPF `MenuItem` already supports `InputGestureText` (display-only). Keep the `KeyBinding`s from Task 51 as the functional source. Example: Refresh `F5`, Fetch `F6`, Update `Ctrl+F7`, Rename `F2`, Move Up `Alt+Up`, Remove `Delete`, and so on.

## 2. Add shortcuts to toolbar tooltips

Example: `Refresh selected repository (F5)`, `Fetch all repositories (Shift+F6)`, `Update all safe repositories (Ctrl+Shift+F7)`. This gives mouse users a natural way to gradually discover the keyboard interface.

## 3. Add F1 keyboard reference

Use WPF's existing Help routed command rather than introducing help-dialog state into `MainWindowViewModel`:

```xml
<Window.CommandBindings>
    <CommandBinding Command="ApplicationCommands.Help"
                    Executed="Help_Executed" />
</Window.CommandBindings>
<Window.InputBindings>
    <KeyBinding Gesture="F1"
                Command="ApplicationCommands.Help" />
</Window.InputBindings>
```

`Help_Executed` in `MainWindow.xaml.cs` opens a purely visual `KeyboardShortcutsDialog` (code-behind is appropriate here; no new service interface). The dialog groups keys as Navigation / Repository management / Git operations / Utilities. No configuration/editing of shortcuts is needed.

## 4. Do not build customizable keybindings yet

Explicitly out of scope: shortcut JSON persistence, user-remappable gestures, conflict resolution, settings UI, keyboard profiles, VS Code-style customization. Fixed, sensible defaults provide nearly all the value.

## 5. Strengthen XAML regression tests

Besides testing individual mappings, add uniqueness (`no two KeyBindings share a Gesture`) and expected-set coverage for the full keymap in `MainWindowXamlTests`. The project already uses XAML-parsing tests for UI contracts, so no full UI automation framework is needed.

## 6. Update documentation backlog

`docs/README.md` still labels its backlog as Tasks 1–47 while Tasks 48–50 already exist. Update it to include Milestone 9 (Tasks 48–50) and Milestone 10 (Tasks 51–53).

## Acceptance criteria

- [ ] Context menus and tooltips reveal shortcuts.
- [ ] F1 opens a grouped shortcut reference.
- [ ] Keymap is protected by uniqueness + expected-set regression tests.
- [ ] No customizable-keybinding infrastructure is introduced.
- [ ] Docs backlog covers Tasks 48–53.
