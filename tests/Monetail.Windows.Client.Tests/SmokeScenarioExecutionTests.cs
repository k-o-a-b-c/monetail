using Monetail.WinUI.Smoke;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class SmokeScenarioExecutionTests
{
    private static readonly SmokeScenario[] Catalog = [new("first", "transfer-editor", () => Task.CompletedTask), new("second", "existing", () => Task.CompletedTask)];
    [Fact]
    public void DefaultIncludesEveryScenarioInOrderAndLegacySuitesPartitionIt()
    {
        Assert.Equal(Catalog, SmokeScenarioExecution.Select(Catalog, []));
        Assert.Equal(Catalog.Take(1), SmokeScenarioExecution.Select(Catalog, ["--transfer-editor-only"]));
        Assert.Equal(Catalog.Skip(1), SmokeScenarioExecution.Select(Catalog, ["--existing-only"]));
        Assert.Equal(Catalog.Skip(1), SmokeScenarioExecution.Select(Catalog, ["--scenario", "second"]));
    }
    [Theory]
    [InlineData("--scenario")]
    [InlineData("--scenario", "unknown")]
    [InlineData("--scenario", "")]
    [InlineData("--scenario", "first", "--scenario", "second")]
    [InlineData("--scenario", "first", "--transfer-editor-only")]
    [InlineData("--scenario", "first", "--list-scenarios")]
    [InlineData("--transfer-editor-only", "--existing-only")]
    public void InvalidSelectionNeverExecutesAnything(params string[] args) =>
        Assert.Throws<ArgumentException>(() => SmokeScenarioExecution.Select(Catalog, args));
    [Fact]
    public async Task SuccessfulScenarioRecordsOnlyItsChecksAndWaitsForCompletion()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 10; SmokeScenarioResult? recorded = null;
        var scenario = new SmokeScenario("work", "existing", async () => { await gate.Task; count += 2; });
        var wall = System.Diagnostics.Stopwatch.StartNew();
        var run = SmokeScenarioExecution.RunAsync(scenario, () => count, r => recorded = r);
        Assert.False(run.IsCompleted); Assert.Null(recorded);
        gate.SetResult(); await run; wall.Stop();
        Assert.NotNull(recorded); Assert.True(recorded.Passed); Assert.Equal(2, recorded.Checks);
        Assert.Equal("work", recorded.Name); Assert.Equal("existing", recorded.Suite);
        Assert.True(double.IsFinite(recorded.ElapsedMilliseconds)); Assert.InRange(recorded.ElapsedMilliseconds, 0, wall.Elapsed.TotalMilliseconds);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAndCancellationRecordElapsedTimeAndPropagateOriginalException(bool cancellation)
    {
        Exception expected = cancellation ? new OperationCanceledException() : new InvalidOperationException("synthetic");
        var count = 2; SmokeScenarioResult? recorded = null;
        var scenario = new SmokeScenario("failure", "existing", () => { count++; return Task.FromException(expected); });
        var actual = await Record.ExceptionAsync(() => SmokeScenarioExecution.RunAsync(scenario, () => count, r => recorded = r));
        Assert.Same(expected, actual); Assert.NotNull(recorded); Assert.False(recorded.Passed); Assert.Equal(1, recorded.Checks);
        Assert.True(double.IsFinite(recorded.ElapsedMilliseconds)); Assert.True(recorded.ElapsedMilliseconds >= 0);
    }
}
