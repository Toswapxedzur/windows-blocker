namespace WindowsBlocker.Bridge;

// Native WebView commands arrive on WPF's UI thread. Kestrel start/stop has a
// synchronous prefix, so run it off that thread and preserve command order.
internal sealed class HubLifecycleQueue(Func<Task> start, Func<Task> stop)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SetRunningAsync(bool running, Func<bool>? mayStart = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (running)
            {
                if (mayStart is null || mayStart()) await Task.Run(start).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(stop).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
