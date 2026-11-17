using System.Diagnostics;

namespace Monetail.WinUI.Smoke;

internal sealed record SmokeScenario(string Name, string Suite, Func<Task> Run);
internal sealed record SmokeScenarioResult(string Name, string Suite, bool Passed, double ElapsedMilliseconds, int Checks);

/// <summary>既存のシナリオを選択・計測するだけ。確認内容・再試行・タイムアウトは変更しない。</summary>
internal static class SmokeScenarioExecution
{
    internal static SmokeScenario[] Select(SmokeScenario[] catalog, string[] arguments)
    {
        var index = Array.IndexOf(arguments, "--scenario");
        var transfer = arguments.Contains("--transfer-editor-only");
        var existing = arguments.Contains("--existing-only");
        if (arguments.Count(a => a == "--scenario") > 1 || transfer && existing ||
            index >= 0 && (transfer || existing || arguments.Contains("--list-scenarios")))
            throw new ArgumentException("シナリオ指定とスイート指定は併用できません。");
        if (index >= 0)
        {
            if (index + 1 >= arguments.Length || !catalog.Any(s => s.Name == arguments[index + 1]))
                throw new ArgumentException("不正なシナリオ名です。--list-scenariosで確認してください。");
            return catalog.Where(s => s.Name == arguments[index + 1]).ToArray();
        }
        return catalog.Where(s => !transfer && !existing || s.Suite == (transfer ? "transfer-editor" : "existing")).ToArray();
    }

    internal static async Task RunAsync(SmokeScenario scenario, Func<int> checkCount, Action<SmokeScenarioResult> record)
    {
        var before = checkCount(); var timer = Stopwatch.StartNew(); var passed = false;
        try { await scenario.Run(); passed = true; }
        finally
        {
            timer.Stop();
            record(new(scenario.Name, scenario.Suite, passed, timer.Elapsed.TotalMilliseconds, checkCount() - before));
        }
    }
}
