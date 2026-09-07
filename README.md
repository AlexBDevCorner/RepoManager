# RepoManager

A Windows desktop dashboard for managing and monitoring multiple local Git repositories from one place.

RepoManager gives you a quick overview of the repositories you actively work with: which branch is checked out, whether the working tree is clean, whether the branch is ahead or behind its upstream, when the repository was last fetched, and whether it can be updated safely.

The application is deliberately **not** a full Git client. Its focus is repository visibility, convenient fetching, and conservative fast-forward updates.

> **Safety principle:** RepoManager may inspect freely and fetch freely, but it only mutates the checked-out branch when it can prove that the update is a safe fast-forward.

## Features

### Repository dashboard

Track only the repositories you care about and see their current state in one place.

For each repository RepoManager can show:

* Current branch or detached HEAD state
* Configured upstream branch
* Remote default branch
* Ahead/behind counts against the upstream
* Ahead/behind counts against the remote default branch
* Clean or dirty working-tree state
* Active merge, rebase, or cherry-pick state
* Last successful fetch time
* Update eligibility
* Human-readable status explanations
* Git errors and useful troubleshooting hints

Selecting a repository opens a details panel with the underlying status information.

### Add and organize repositories

Repositories can be added individually or several folders can be selected at once.

RepoManager also supports:

* **Repository discovery** — scan a parent directory for Git repositories
* **Custom aliases** — display a friendly name instead of the directory name
* **Custom ordering** — move repositories up or down
* **Persistent ordering** — dashboard order is restored on the next launch
* Removing repositories from the dashboard without touching the repository itself

Repository aliases and ordering are dashboard metadata only. They do not modify the Git repository.

### Refresh and fetch

Repository information can be refreshed without contacting the remote:

* **Refresh** — inspect one repository
* **Refresh All** — inspect every configured repository

Remote references can be updated separately:

* **Fetch** — fetch one repository
* **Fetch All** — fetch all repositories with bounded parallelism

After fetching, RepoManager refreshes the repository status so ahead/behind information reflects the newly downloaded remote refs.

### Safe updates

RepoManager can update repositories that are safe to fast-forward.

Available actions:

* **Update** — update the selected repository when eligible
* **Update Safe Repositories** — update all repositories currently classified as safe

An update is refused when RepoManager detects states such as:

* Local commits ahead of the upstream
* Diverged history
* Dirty working tree
* Missing upstream
* Detached HEAD
* Git operation already in progress
* Missing or invalid repository
* Incompatible remote/upstream configuration

The final mutation is performed using Git's own fast-forward protection:

```text
git pull --ff-only --no-rebase
```

RepoManager does not automatically merge, rebase, reset, stash, or resolve conflicts.

### Convenience actions

For a selected repository you can also:

* Open the repository folder
* Open a terminal in the repository
* Copy the repository path
* Rename its dashboard alias
* Move it up or down
* Remove it from the dashboard

Most repository actions are also available from the row context menu.

### Cancellation and shutdown safety

Long-running batch operations can be cancelled.

RepoManager distinguishes between cancelling normal work and shutting down the application. In particular, once a mutating fast-forward update has reached its critical Git operation, ordinary user cancellation will not terminate `git.exe` halfway through that mutation.

Application shutdown cancels outstanding Git processes and performs cleanup to avoid leaving orphaned processes behind.

## What RepoManager does not do

RepoManager intentionally does **not** try to replace GitKraken, SourceTree, Visual Studio, Rider, or the command line.

Version 1 does not provide:

* Commit
* Push
* Checkout
* Branch creation or deletion
* Merge
* Rebase
* Reset
* Automatic stash
* Conflict resolution
* Repository cloning
* File editing
* Diff viewing
* Git history browsing
* GitHub integration
* Azure DevOps integration
* Pull-request management

The goal is a **repository status dashboard and safe updater**, not a general-purpose Git client.

## Requirements

### Running from source

* Windows
* [.NET 10 SDK](https://dotnet.microsoft.com/)
* Git for Windows available through `git.exe`

The application is built with WPF and targets:

```text
net10.0-windows
```

### Published build

RepoManager can be published as a self-contained Windows x64 application, so the target machine does not need the .NET runtime installed separately.

Git is still required because RepoManager intentionally uses the installed `git.exe` rather than embedding its own Git implementation.

## Running from source

Clone the repository:

```powershell
git clone https://github.com/AlexBDevCorner/RepoManager.git
cd RepoManager
```

Run the application:

```powershell
dotnet run --project src/RepoDashboard.App
```

Or build the complete solution first:

```powershell
dotnet build RepoDashboard.slnx
```

## Creating a Windows build

Create a self-contained Windows x64 build with:

```powershell
dotnet publish src/RepoDashboard.App -c Release -r win-x64 --self-contained true
```

The resulting publish directory contains the Windows executable and everything required by the .NET application.

## Testing

Run the complete test suite with:

```powershell
dotnet test RepoDashboard.slnx
```

The project contains unit and integration coverage for areas including:

* Repository inspection
* Git command execution
* Ahead/behind calculation
* Update eligibility
* Fast-forward updates
* Fetching and batch operations
* Cancellation
* Shutdown behavior
* Git failure classification
* Repository discovery
* Configuration persistence
* Aliases and ordering
* View-model behavior

Integration tests create temporary Git repositories and exercise real Git behavior rather than mocking every interaction.

## Configuration and local data

RepoManager stores its own configuration under:

```text
%LOCALAPPDATA%\RepoDashboard\
```

### `repositories.json`

Contains the repositories shown on the dashboard, including:

* Repository ID
* Repository path
* Display name / alias
* Repository order

The array order is the dashboard order.

### `state.json`

Contains operational metadata such as the last successful fetch timestamp.

RepoManager does **not** store transient Git state as application configuration. Branches, commits, working-tree state, upstreams, and divergence are read from Git itself.

## Architecture

The solution is intentionally small and separated into three projects:

```text
src/
├── RepoDashboard.App/
├── RepoDashboard.Core/
└── RepoDashboard.Infrastructure/
```

### `RepoDashboard.App`

WPF presentation layer.

Contains:

* Views and dialogs
* View models
* Desktop-specific services
* Dependency-injection composition root

### `RepoDashboard.Core`

Application and domain logic.

Contains:

* Repository inspection
* Dashboard services
* Update eligibility rules
* Fetch/update orchestration
* Discovery abstractions
* Domain models

Core has no dependency on WPF or Infrastructure.

### `RepoDashboard.Infrastructure`

External-system implementations.

Contains:

* `git.exe` process execution
* JSON persistence
* Operational state persistence

Git commands are executed directly through `System.Diagnostics.Process`. RepoManager does not use LibGit2Sharp or shell commands through PowerShell/cmd.

## Technology

* .NET 10
* C#
* WPF
* CommunityToolkit.Mvvm
* Microsoft.Extensions.Hosting
* Microsoft.Extensions.DependencyInjection
* Microsoft.Extensions.Logging
* System.Text.Json
* xUnit
* Git CLI

## Design philosophy

RepoManager favors predictable behavior over Git automation.

A few rules guide the project:

1. **Reading state is cheap and safe.**
2. **Fetching is allowed because it does not change the checked-out branch.**
3. **Updating requires explicit proof that a fast-forward is safe.**
4. **Git's `--ff-only` remains the final safety net.**
5. **Unexpected repository states should be explained, not automatically repaired.**
6. **The application should never hide Git complexity by performing destructive operations behind the user's back.**

## Documentation

More detailed design documentation is available under [`docs/`](docs/):

* [Goal and scope](docs/01-goal-and-scope.md)
* [Technical requirements](docs/02-tech-requirements.md)
* [Solution architecture](docs/03-solution-architecture.md)
* [High-level architecture](docs/04-high-level-architecture.md)
* [Domain concepts](docs/05-domain-concepts.md)
* [Architectural rules](docs/07-architectural-rules.md)
* [Documentation and implementation task index](docs/README.md)

## Project status

RepoManager is functional as a Windows repository dashboard and safe updater.

The current application supports the complete workflow of:

```text
Add / Discover repositories
        ↓
Inspect local state
        ↓
Refresh or Fetch
        ↓
Understand ahead/behind status
        ↓
Classify update safety
        ↓
Fast-forward eligible repositories
```

Future features should preserve the project's core rule: **convenience must not come at the cost of surprising or unsafe Git mutations.**
