using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class GitCommandRunnerTests : IDisposable
{
    private readonly IGitCommandRunner _runner = new GitCommandRunner();
    private readonly string _workingDirectory;

    public GitCommandRunnerTests()
    {
        _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_workingDirectory);
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(_workingDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_GitVersion_ReturnsSuccessWithVersionOutput()
    {
        // Arrange & Act
        var result = await _runner.ExecuteAsync(_workingDirectory, ["--version"]);

        // Assert
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().StartWith("git version");
        result.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownRef_ReturnsNonZeroWithoutThrowing()
    {
        // Arrange
        var init = await _runner.ExecuteAsync(_workingDirectory, ["init"]);
        init.Success.Should().BeTrue();

        // Act
        var result = await _runner.ExecuteAsync(
            _workingDirectory,
            ["rev-parse", "--verify", "refs/heads/does-not-exist"]);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.StandardError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelled_ThrowsWithoutStartingGit()
    {
        // Arrange: a working directory that could never host a process
        // start, combined with an already-cancelled token.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var missingDirectory = Path.Combine(_workingDirectory, "does-not-exist");

        // Act
        var act = () => _runner.ExecuteAsync(
            missingDirectory, ["--version"], cancellation.Token);

        // Assert: cancellation is observed before git.exe is ever launched —
        // without the guard this would throw Win32Exception for the bad
        // working directory instead.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_KillsGitProcessAndThrows()
    {
        // Arrange: git daemon blocks until killed, so it is a
        // deterministic stand-in for a long-running git process.
        // Note: this verifies the git process itself is terminated
        // (its listener port closes). Full descendant-tree verification
        // is out of scope until the Task 5 test infrastructure lands.
        var port = GetFreeTcpPort();

        using var cancellation = new CancellationTokenSource();

        var executeTask = _runner.ExecuteAsync(
            _workingDirectory,
            ["daemon", "--reuseaddr", $"--port={port}", "--export-all", $"--base-path={_workingDirectory}"],
            cancellation.Token);

        await WaitForPortStateAsync(port, open: true, TestTimeout);
        await cancellation.CancelAsync();

        // Act
        var act = () => executeTask;

        // Assert: cancellation propagates and the daemon is gone.
        await act.Should().ThrowAsync<OperationCanceledException>();
        await WaitForPortStateAsync(port, open: false, TestTimeout);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForPortStateAsync(int port, bool open, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            if (await IsPortOpenAsync(port) == open)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                FailPortState(port, open);
            }

            await Task.Delay(100);
        }
    }

    private static async Task<bool> IsPortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static void FailPortState(int port, bool open)
    {
        var expected = open ? "accept connections" : "refuse connections";
        throw new TimeoutException(
            $"Timed out waiting for 127.0.0.1:{port} to {expected}.");
    }
}
