namespace WindowsBlocker.Enforcement;

// Pure process-instance ledger, shared by ticks, window events and v.quit.
// PID reuse cannot inherit another process's refusal to close.
public sealed class QuitRequestScheduler
{
    private readonly Dictionary<string, DateTimeOffset> _askedAt = new();
    private readonly object _gate = new();
    public bool ShouldRequest(string instance, DateTimeOffset now, TimeSpan retry)
    {
        lock (_gate)
        {
            if (_askedAt.TryGetValue(instance, out var asked) && (retry <= TimeSpan.Zero || now - asked < retry)) return false;
            _askedAt[instance] = now;
            return true;
        }
    }
    public void Reconcile(IReadOnlySet<string> runningInstances)
    {
        lock (_gate)
            foreach (var instance in _askedAt.Keys.Where(i => !runningInstances.Contains(i)).ToList()) _askedAt.Remove(instance);
    }
}
