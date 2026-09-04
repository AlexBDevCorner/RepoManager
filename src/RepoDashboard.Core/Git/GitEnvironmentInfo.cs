namespace RepoDashboard.Core.Git;

public sealed record GitEnvironmentInfo(
    bool Available,
    string? Version,
    string? Error);
