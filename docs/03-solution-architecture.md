# 03 — Solution Architecture

## Solution layout

```text
RepoDashboard.sln

src/
    RepoDashboard.App/
    RepoDashboard.Core/
    RepoDashboard.Infrastructure/

tests/
    RepoDashboard.Core.Tests/
    RepoDashboard.IntegrationTests/
```

## Project responsibilities

```text
RepoDashboard.App
    WPF
    ViewModels
    Views
    commands
    dialogs
    presentation

RepoDashboard.Core
    domain models
    interfaces
    repository inspection logic
    status classification
    safe-update rules
    application orchestration

RepoDashboard.Infrastructure
    git.exe execution
    filesystem
    JSON persistence
    logging
```

## Dependency direction

```text
App
 ↓
Core
 ↑
Infrastructure
```

More precisely:

```text
App ───────────────→ Core
Infrastructure ────→ Core
```

- `Core` must have no project references.
- The application project (`App`) is the composition root and wires implementations together.

## Project references

```text
App → Core
App → Infrastructure

Infrastructure → Core

Core.Tests → Core

IntegrationTests → Core
IntegrationTests → Infrastructure
```

## NuGet packages

App:

```text
CommunityToolkit.Mvvm
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Hosting
Microsoft.Extensions.Logging
```

Tests:

```text
FluentAssertions
```

Optional:

```text
Microsoft.Extensions.Logging.Debug
```

## Suggested directory structure

```text
RepoDashboard.Core/
    Git/
    Models/
    Repositories/
    Sync/

RepoDashboard.Infrastructure/
    Git/
    Configuration/
    Logging/

RepoDashboard.App/
    Views/
    ViewModels/
    Services/
```
