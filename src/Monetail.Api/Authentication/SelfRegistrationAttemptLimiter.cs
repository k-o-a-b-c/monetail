namespace Monetail.Api.Authentication;

/// <summary>自己登録開始をIPごとに直近10分5回へ制限し、保持するIP数も制限する。</summary>
public sealed class SelfRegistrationAttemptLimiter(TimeProvider time)
{
    private readonly object gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> attempts = new(StringComparer.Ordinal);

    /// <summary>期限切れの試行を除去し、最大4096キーの範囲で待機せず許可・拒否する。</summary>
    public bool TryAcquire(string address)
    {
        lock (gate)
        {
            var cutoff = time.GetUtcNow().AddMinutes(-10);
            foreach (var pair in attempts.ToArray())
            {
                while (pair.Value.TryPeek(out var at) && at <= cutoff) pair.Value.Dequeue();
                if (pair.Value.Count == 0) attempts.Remove(pair.Key);
            }
            if (!attempts.TryGetValue(address, out var history))
            {
                if (attempts.Count >= 4096) return false;
                history = new();
                attempts.Add(address, history);
            }
            if (history.Count >= 5) return false;
            history.Enqueue(time.GetUtcNow());
            return true;
        }
    }
}
