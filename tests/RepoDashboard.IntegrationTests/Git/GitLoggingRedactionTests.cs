using FluentAssertions;
using Microsoft.Extensions.Logging;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

/// <summary>
/// Review fix: raw Git output can embed credential-bearing URLs, so it must
/// stay in the UI details model and never reach application logs. These tests
/// capture log events and assert the secret is absent while diagnostics
/// (exit code, classified failure kind) are still present.
/// </summary>
public sealed class GitLoggingRedactionTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<string> Events = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var rendered = formatter(state, exception);
            Events.Add($"{logLevel}: {rendered}");

            // Structured arguments are formatted separately by most
            // providers — capture each value too so a secret smuggled as
            // an argument value cannot slip through unnoticed.
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (pair.Value is not null)
                    {
                        Events.Add($"{logLevel}: [{pair.Key}]={pair.Value}");
                    }
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class SecretRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> ExecuteAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCommandResult
            {
                ExitCode = 128,
                StandardOutput = string.Empty,
                StandardError =
                    "fatal: unable to access " +
                    "'https://user:super-secret@example.invalid/repo.git/': " +
                    "The requested URL returned error: 403",
                Duration = TimeSpan.FromMilliseconds(5)
            });
    }

    private static RepositoryConfiguration Config() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Store",
            Path = """C:\Source\Repos\Store""",
            PreferredRemote = "origin"
        };

    [Fact]
    public async Task Fetcher_failure_keeps_secret_in_model_but_not_in_logs()
    {
        var logger = new CapturingLogger<RepositoryFetcher>();
        IRepositoryFetcher sut = new RepositoryFetcher(new SecretRunner(), logger);

        var result = await sut.FetchAsync(Config(), CancellationToken.None);

        // UI model preserves raw diagnostics for the details panel.
        result.Success.Should().BeFalse();
        result.RawOutput.Should().Contain("super-secret");
        result.Message.Should().Contain("super-secret");
        result.FriendlyHint.Should().Be(
            "Authentication failed. Check Git Credential Manager or SSH credentials.");

        // No log event may carry the credential.
        logger.Events.Should().NotBeEmpty();
        logger.Events.Should().OnlyContain(e => !e.Contains("super-secret"));

        // Structured diagnostics are still logged.
        logger.Events.Should().Contain(e => e.Contains("128"));
        logger.Events.Should().Contain(e => e.Contains("Authentication"));
    }

    [Fact]
    public async Task Updater_fetch_failure_keeps_secret_in_model_but_not_in_logs()
    {
        var fetchLogger = new CapturingLogger<RepositoryFetcher>();
        var updateLogger = new CapturingLogger<RepositoryUpdater>();
        var fetcher = new RepositoryFetcher(new SecretRunner(), fetchLogger);
        var inspector = new RepositoryInspector(
            new GitCommandRunner(), new DivergenceCalculator(new GitCommandRunner()));
        var updater = new RepositoryUpdater(
            new GitCommandRunner(), fetcher, inspector,
            new UpdateEligibilityClassifier(), updateLogger);

        var result = await updater.UpdateAsync(Config(), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        result.Message.Should().Contain("super-secret");

        var allEvents = fetchLogger.Events.Concat(updateLogger.Events).ToList();
        allEvents.Should().NotBeEmpty();
        allEvents.Should().OnlyContain(e => !e.Contains("super-secret"));
    }

    [Fact]
    public async Task Fetcher_never_logs_preferred_remote_value()
    {
        // PreferredRemote is unconstrained config: it can hold a
        // credential-bearing URL, and is passed to Git as an argument.
        // It must never reach application logs (Information or Debug).
        // Use a real temp git repo so the runner actually starts and logs.
        var workDir = Path.Combine(
            Path.GetTempPath(), "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var bootstrap = new GitCommandRunner();
            var init = await bootstrap.ExecuteAsync(workDir, ["init"]);
            init.Success.Should().BeTrue();

            var runnerLogger = new CapturingLogger<GitCommandRunner>();
            var fetchLogger = new CapturingLogger<RepositoryFetcher>();
            var runner = new GitCommandRunner(runnerLogger);
            IRepositoryFetcher sut = new RepositoryFetcher(runner, fetchLogger);

            var config = Config() with
            {
                Path = workDir,
                PreferredRemote = "https://user:super-secret@example.invalid/repo.git"
            };

            await sut.FetchAsync(config, CancellationToken.None);

            var allEvents = fetchLogger.Events.Concat(runnerLogger.Events).ToList();
            allEvents.Should().NotBeEmpty();
            allEvents.Should().OnlyContain(e => !e.Contains("super-secret"));
        }
        finally
        {
            TestDirectories.DeleteRecursively(workDir);
        }
    }

    [Fact]
    public async Task Runner_logs_only_verb_never_full_arguments()
    {
        var logger = new CapturingLogger<GitCommandRunner>();
        var runner = new GitCommandRunner(logger);
        var markerDir = Path.Combine(
            Path.GetTempPath(), "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerDir);

        try
        {
            // Secret smuggled as a non-verb argument: fast local failure,
            // no network. Only "rev-parse" may be logged.
            await runner.ExecuteAsync(
                markerDir,
                ["rev-parse", "--verify", "https://user:super-secret@example.invalid/ref"]);
        }
        finally
        {
            TestDirectories.DeleteRecursively(markerDir);
        }

        logger.Events.Should().NotBeEmpty();
        logger.Events.Should().OnlyContain(e => !e.Contains("super-secret"));
        logger.Events.Should().Contain(e => e.Contains("rev-parse"));
    }

    [Fact]
    public void Sanitizer_redacts_uri_user_info()
    {
        GitLogSanitizer.Sanitize(
            "fatal: unable to access 'https://user:super-secret@example.invalid/x': 403")
            .Should().NotContain("super-secret");

        GitLogSanitizer.Sanitize(
            "fatal: unable to access 'https://user:super-secret@example.invalid/x': 403")
            .Should().Contain("://***@");

        GitLogSanitizer.Sanitize("plain message").Should().Be("plain message");
        GitLogSanitizer.Sanitize(null).Should().BeNull();
    }

    [Fact]
    public async Task Runner_debug_log_contains_no_working_directory()
    {
        var logger = new CapturingLogger<GitCommandRunner>();
        var runner = new GitCommandRunner(logger);
        var markerDir = Path.Combine(
            Path.GetTempPath(), "RepoDashboard.Tests",
            "secret-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerDir);

        try
        {
            await runner.ExecuteAsync(markerDir, ["--version"]);
        }
        finally
        {
            TestDirectories.DeleteRecursively(markerDir);
        }

        logger.Events.Should().NotBeEmpty();
        logger.Events.Should().OnlyContain(e => !e.Contains(markerDir));
    }
}
