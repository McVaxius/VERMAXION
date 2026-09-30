// Imported/adapted from Jaksuhn/clib 1.0.42 (c05463985b19f2e63fcba3618f33281b1e2bd00b).
// Only the cancellable framework coroutine and task-owned cleanup required by VSatisfy are retained.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VERMAXION.CustomDeliveries;

namespace VERMAXION.DeliverySupport;

public abstract class AutoTask
{
    protected readonly struct DebugContext : IDisposable
    {
        private readonly AutoTask context;
        private readonly int depth;
        public DebugContext(AutoTask task, string name)
        {
            context = task;
            depth = task.debugContext.Count;
            task.debugContext.Add(name);
        }
        public void Dispose()
        {
            if (depth < context.debugContext.Count)
                context.debugContext.RemoveRange(depth, context.debugContext.Count - depth);
        }
    }

    public string Status { get; protected set; } = string.Empty;
    public string? Failure { get; private set; }
    public bool CompletedSuccessfully { get; private set; }
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<string> debugContext = [];
    private readonly List<IDisposable> disposables = [];
    private static readonly AsyncLocal<AutoTask?> activeTask = new();
    internal static AutoTask? ActiveTask => activeTask.Value;
    internal void RegisterCleanup(IDisposable disposable) => disposables.Add(disposable);
    protected CancellationToken CancelToken => cancellation.Token;

    private void Cleanup()
    {
        for (var i = disposables.Count - 1; i >= 0; --i)
        {
            try { disposables[i].Dispose(); }
            catch (Exception ex) { Service.Log.Error(ex, "Custom delivery cleanup failed"); }
        }
        disposables.Clear();
    }

    public void Cancel()
    {
        cancellation.Cancel();
        Cleanup();
    }

    public void Run(Action completed) => _ = Service.Framework.Run(async () =>
    {
        activeTask.Value = this;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            await Execute();
            cancellation.Token.ThrowIfCancellationRequested();
            CompletedSuccessfully = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Failure = ex.Message;
            Service.Log.Error(ex, "Custom delivery task failed");
        }
        finally
        {
            Cleanup();
            completed();
            activeTask.Value = null;
        }
    });

    protected abstract Task Execute();
    protected Task NextFrame(int frames = 1)
    {
        CancelToken.ThrowIfCancellationRequested();
        return Service.Framework.DelayTicks(frames, CancelToken);
    }
    protected async Task WaitWhile(Func<bool> condition, string scopeName)
    {
        using var scope = BeginScope(scopeName);
        var began = DateTime.UtcNow;
        while (condition())
        {
            CancelToken.ThrowIfCancellationRequested();
            // A single bounded wait fails the current route; it does not retry or relog.
            if (DateTime.UtcNow - began > TimeSpan.FromMinutes(30))
                Error($"Timed out: {scopeName}");
            await NextFrame();
        }
    }
    protected Task WaitUntil(Func<bool> condition, string scopeName) => WaitWhile(() => !condition(), scopeName);
    protected DebugContext BeginScope(string name) => new(this, name);
    protected void Log(string message) => Service.Log.Debug($"[CustomDeliveries] [{string.Join(" > ", debugContext)}] {message}");
    protected void Warning(string message) => Service.Log.Warning($"[CustomDeliveries] {message}");
    protected void Error(string message) => throw new InvalidOperationException($"[{GetType().Name}] {message}");
    protected void ErrorIf(bool condition, string message) { if (condition) Error(message); }
}

public sealed class Automation : IDisposable
{
    public AutoTask? CurrentTask { get; private set; }
    public bool Running => CurrentTask != null;
    public void Start(AutoTask task, Action<AutoTask> completed)
    {
        Stop();
        CurrentTask = task;
        task.Run(() =>
        {
            if (CurrentTask != task) return;
            CurrentTask = null;
            completed(task);
        });
    }
    public void Stop()
    {
        CurrentTask?.Cancel();
        CurrentTask = null;
    }
    public void Dispose() => Stop();
}

public sealed class OnDispose : IDisposable
{
    private readonly Action cleanup;
    private int ran;
    public OnDispose(Action action)
    {
        cleanup = action;
        AutoTask.ActiveTask?.RegisterCleanup(this);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref ran, 1) == 0) cleanup();
    }
}