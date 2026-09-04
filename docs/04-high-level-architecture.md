# 04 — High-Level Architecture

```mermaid
flowchart TD
    UI[WPF UI]

    VM[ViewModels]

    Dashboard[Repository Dashboard Service]
    Inspector[Repository Inspector]
    Classifier[Update Eligibility Classifier]
    Fetcher[Repository Fetcher]
    Updater[Repository Updater]

    Config[Repository Configuration Store]
    Git[Git Command Runner]

    EXE[git.exe]
    JSON[repositories.json]

    UI --> VM
    VM --> Dashboard

    Dashboard --> Inspector
    Dashboard --> Fetcher
    Dashboard --> Updater

    Updater --> Classifier

    Inspector --> Git
    Fetcher --> Git
    Updater --> Git

    Dashboard --> Config

    Git --> EXE
    Config --> JSON
```

## Critical rule

> ViewModels never execute Git commands.

Bad:

```csharp
await Process.Start("git", "pull");
```

inside:

```csharp
RepositoryRowViewModel
```

Good:

```csharp
await _repositoryDashboard.UpdateAsync(repositoryId);
```

The ViewModel knows what the user wants. The application/core services decide how to perform it.
