# Task 1 — Create the solution

- Milestone: 1 — Git foundation
- Type: tech-setup

## Goal

Create a clean solution with correct dependency boundaries before writing Git logic.

## Steps

Create projects:

```powershell
dotnet new sln -n RepoDashboard

dotnet new wpf -n RepoDashboard.App -f net10.0-windows

dotnet new classlib -n RepoDashboard.Core -f net10.0

dotnet new classlib -n RepoDashboard.Infrastructure -f net10.0

dotnet new xunit -n RepoDashboard.Core.Tests -f net10.0

dotnet new xunit -n RepoDashboard.IntegrationTests -f net10.0
```

Add them to the solution.

## Dependencies

```text
App → Core
App → Infrastructure
Infrastructure → Core
Core.Tests → Core
IntegrationTests → Core
IntegrationTests → Infrastructure
```

Core must have no project references.

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

## Rules

Enable:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

Do not disable nullable warnings.

## Acceptance criteria

- [ ] Solution builds.
- [ ] WPF application launches.
- [ ] Core contains no WPF dependencies.
- [ ] Infrastructure references Core.
- [ ] App references Core and Infrastructure.
