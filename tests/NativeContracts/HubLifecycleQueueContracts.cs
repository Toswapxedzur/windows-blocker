using WindowsBlocker.Bridge;

internal static class HubLifecycleQueueContracts
{
    public static async Task Run()
    {
        var callerThread = 0;
        var stopCalls = 0;
        var stopEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var startCalls = 0;
        var queue = new HubLifecycleQueue(
            () =>
            {
                events.Add("start");
                Interlocked.Increment(ref startCalls);
                return Task.CompletedTask;
            },
            async () =>
            {
                if (Interlocked.Increment(ref stopCalls) == 1 && Environment.CurrentManagedThreadId == callerThread)
                    throw new Exception("Hub stop ran on the WebView caller thread");
                events.Add("stop-entered");
                stopEntered.TrySetResult(true);
                await releaseStop.Task;
                events.Add("stop-finished");
            });

        // The native UI must return from the stop callback before its listener
        // completes. A following Start cannot overtake that Stop.
        Task? stop = null;
        Exception? callerError = null;
        using var callerReturned = new ManualResetEventSlim();
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            try { stop = queue.SetRunningAsync(false); }
            catch (Exception ex) { callerError = ex; }
            finally { callerReturned.Set(); }
        });
        caller.Start();
        if (!callerReturned.Wait(TimeSpan.FromSeconds(5)) || !caller.Join(TimeSpan.FromSeconds(5)))
            throw new Exception("Native caller blocked during Stop");
        if (callerError is not null) throw callerError;
        if (stop is null || stop.IsCompleted) throw new Exception("Stop did not yield while listener was active");
        await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var start = queue.SetRunningAsync(true);
        if (start.IsCompleted || startCalls != 0)
            throw new Exception("Start overtook an active Stop");
        releaseStop.SetResult(true);
        await Task.WhenAll(stop, start).WaitAsync(TimeSpan.FromSeconds(5));
        if (events.Count != 3 || events[0] != "stop-entered" || events[1] != "stop-finished" || events[2] != "start")
            throw new Exception("Hub command order changed");

        // Closing has priority over a Start already queued behind Stop.
        stopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        releaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closingStop = queue.SetRunningAsync(false);
        await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var lateStart = queue.SetRunningAsync(true, () => false);
        if (lateStart.IsCompleted) throw new Exception("Closing Start overtook Stop");
        releaseStop.SetResult(true);
        await Task.WhenAll(closingStop, lateStart).WaitAsync(TimeSpan.FromSeconds(5));
        if (startCalls != 1) throw new Exception("Queued Start reopened the hub after closing");
        Console.WriteLine("PASS Hub Stop yields the UI, then Start follows Stop and closing suppresses restart");
    }
}
