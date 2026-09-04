using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;

namespace RepoDashboard.Infrastructure.Git;

public sealed class DivergenceCalculator : IDivergenceCalculator
{
    private readonly IGitCommandRunner _runner;

    public DivergenceCalculator(IGitCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<Divergence?> CalculateAsync(
        string repositoryPath,
        string leftRef,
        string rightRef,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(leftRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightRef);

        var result = await _runner.ExecuteAsync(
            repositoryPath,
            ["rev-list", "--left-right", "--count", $"{leftRef}...{rightRef}"],
            cancellationToken);

        // A non-zero exit normally means one of the refs does not resolve
        // (for example a locally created branch with no remote counterpart
        // fetched yet). Divergence is then unknown — not an application error.
        if (!result.Success)
        {
            return null;
        }

        return ParseDivergence(result.StandardOutput);
    }

    private static Divergence ParseDivergence(
        string output)
    {
        var parts = output.Split(
            [' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            throw new InvalidOperationException(
                $"Unexpected rev-list output: {output}");
        }

        return new Divergence(
            Ahead: int.Parse(parts[0]),
            Behind: int.Parse(parts[1]));
    }
}
