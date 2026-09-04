# 02 — Tech Requirements

## Use

- .NET 10
- C#
- WPF
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- System.Text.Json
- `git.exe` through `System.Diagnostics.Process`
- xUnit for tests

## Do not use

- LibGit2Sharp
- Entity Framework
- SQLite
- MediatR
- ASP.NET Core
- React
- Electron
- Embedded web servers

Those technologies would add complexity without solving an important problem in this application.

## Global rules

- Enable `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
- Do not disable nullable warnings.
- `Core` must not reference WPF or Infrastructure.
- Execute `git.exe` directly — never through `cmd.exe` / `powershell.exe`.
- Use `ProcessStartInfo.ArgumentList` (never string-concatenated `Arguments`) to avoid quoting issues with spaces and shell characters.
- Prefer machine-readable Git output (`--porcelain`, `--count`, `--short`, `symbolic-ref`, `rev-parse`) over human-readable / localized output.
