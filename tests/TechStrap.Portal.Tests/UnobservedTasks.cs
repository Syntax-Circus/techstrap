namespace TechStrap.Portal.Tests;

/// <summary>
/// Runs requests and returns every exception left in a task nobody observed (D-057). The event is process-wide, so it also hears tests running in parallel: a leak anywhere in the Portal can fail one of these
/// assertions, and the stack trace says where it came from.
/// </summary>
internal static class UnobservedTasks
{
    public static async Task<IReadOnlyList<Exception>> DuringAsync(Func<Task> requests, CancellationToken cancellationToken)
    {
        var unobserved = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs args) { lock (unobserved) { unobserved.AddRange(args.Exception.InnerExceptions); } }
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            await requests();

            // The event is raised by the finalizer of the faulted task, so the work left behind gets a moment to finish and be collected.
            await Task.Delay(200, cancellationToken);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            lock (unobserved)
            {
                return [.. unobserved];
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }
    }
}
