using System.Collections.Concurrent;
using System.Diagnostics;
using WindowsBlocker.Bridge;

internal static class HubStopContextContracts
{
    public static void Run()
    {
        var context = new NonPumpingContext();
        using var entered = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hub = new ConnectionHub
        {
            ListenerStopCompletionForTests = async stopped =>
            {
                await stopped.ConfigureAwait(false);
                entered.Set();
                await release.Task.ConfigureAwait(false);
            }
        };
        Exception? failure = null;
        var owner = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { hub.Stop(); }
            catch (Exception error) { failure = error; }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
                returned.Set();
            }
        });
        var incompleteStopObserved = false;
        var completedWithoutPump = false;
        var assistedFailureCleanup = false;
        owner.Start();
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(5)))
                throw new Exception("Hub stop did not reach the controlled asynchronous listener completion");
            // The sole owner-thread blocking call is Stop's GetResult. Do not
            // release the task before its await continuation has been registered.
            if (!SpinWait.SpinUntil(
                    () => (owner.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5)))
                throw new Exception("The original stop caller did not block on the incomplete stop task");
            incompleteStopObserved = true;
            release.SetResult(true);
            completedWithoutPump = returned.Wait(TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.TrySetResult(true);
            // A failing old implementation queues its continuation to this
            // unpumped context. Run only this fixture's queued work so its
            // original owner thread can join normally; this is not acceptance.
            var cleanup = Stopwatch.StartNew();
            while (!returned.IsSet && cleanup.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (context.RunOne()) assistedFailureCleanup = true;
                else Thread.Sleep(1);
            }
            if (!owner.Join(TimeSpan.FromSeconds(5)))
                throw new Exception("Original hub-stop fixture thread remains pending");
        }
        Console.WriteLine(
            $"OBSERVED hub-stop incomplete={incompleteStopObserved} completedWithoutPump={completedWithoutPump} " +
            $"posted={context.Posted} assistedFailureCleanup={assistedFailureCleanup} originalThreadJoined=True");
        if (failure != null) throw new Exception("Original synchronous hub stop failed", failure);
        if (!incompleteStopObserved || !completedWithoutPump || context.Posted != 0 || assistedFailureCleanup)
            throw new Exception("Synchronous hub stop captured the caller context and required a pump");
        Console.WriteLine("PASS synchronous hub stop completes without pumping the caller context");
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
        private int _posted;
        public int Posted => Volatile.Read(ref _posted);
        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posted);
            _pending.Enqueue((callback, state));
        }
        public bool RunOne()
        {
            if (!_pending.TryDequeue(out var work)) return false;
            work.Callback(work.State);
            return true;
        }
    }
}
