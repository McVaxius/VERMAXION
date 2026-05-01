using System;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class MomIPCClient
{
    private static readonly TimeSpan ReadinessCacheDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadinessFailureLogThrottle = TimeSpan.FromSeconds(30);

    private readonly IPluginLog log;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICallGateSubscriber<string> readinessSubscriber;
    private readonly ICallGateSubscriber<bool> isReadySubscriber;
    private readonly ICallGateSubscriber<string> statusSubscriber;
    private readonly ICallGateSubscriber<string, string> startRunSubscriber;
    private readonly ICallGateSubscriber<int, string, string> startRunsSubscriber;
    private readonly ICallGateSubscriber<int, string, bool, string> startRunsWithOptionsSubscriber;
    private readonly ICallGateSubscriber<string> cancelSubscriber;
    private MomIpcReadiness cachedReadiness = MomIpcReadiness.NotChecked();
    private DateTime cachedReadinessAtUtc = DateTime.MinValue;
    private string lastReadinessFailureMessage = string.Empty;
    private DateTime lastReadinessFailureLoggedAtUtc = DateTime.MinValue;

    public MomIPCClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        readinessSubscriber = pluginInterface.GetIpcSubscriber<string>("mom.GetReadiness");
        isReadySubscriber = pluginInterface.GetIpcSubscriber<bool>("mom.IsReady");
        statusSubscriber = pluginInterface.GetIpcSubscriber<string>("mom.GetStatus");
        startRunSubscriber = pluginInterface.GetIpcSubscriber<string, string>("mom.StartRun");
        startRunsSubscriber = pluginInterface.GetIpcSubscriber<int, string, string>("mom.StartCcRuns");
        startRunsWithOptionsSubscriber = pluginInterface.GetIpcSubscriber<int, string, bool, string>("mom.StartCcRunsWithOptions");
        cancelSubscriber = pluginInterface.GetIpcSubscriber<string>("mom.CancelActiveRun");
    }

    public bool IsReady()
        => GetReadiness().CanStart;

    public MomIpcReadiness GetReadiness(bool useCache = true)
    {
        var now = DateTime.UtcNow;
        if (useCache &&
            cachedReadinessAtUtc != DateTime.MinValue &&
            now - cachedReadinessAtUtc < ReadinessCacheDuration)
        {
            return cachedReadiness;
        }

        var readiness = FetchReadiness(now);
        cachedReadiness = readiness;
        cachedReadinessAtUtc = now;
        return readiness;
    }

    public MomRunResult GetStatus()
        => InvokeJson(statusSubscriber, MomRunResult.Idle(), "[mom IPC] GetStatus failed");

    public MomRunResult StartCcRuns(int runCount, string job)
        => StartRun(runCount, job, stopAtSeriesRank25: false);

    public MomRunResult StartCcRuns(int runCount, string job, bool stopAtSeriesRank25)
        => StartRun(runCount, job, stopAtSeriesRank25);

    public MomRunResult StartRun(
        int runCount,
        string job,
        bool stopAtSeriesRank25,
        string route = MomRunRoutes.CasualCc,
        string requestedBy = "VERMAXION",
        bool enableAutomation = true)
    {
        var safeJob = job ?? string.Empty;
        var safeRoute = route ?? MomRunRoutes.CasualCc;
        var request = new MomStartRunRequest
        {
            Route = safeRoute,
            RunCount = runCount,
            Job = safeJob,
            StopAtSeriesRank25 = stopAtSeriesRank25,
            RequestedBy = requestedBy,
            EnableAutomation = enableAutomation,
        };

        try
        {
            var requestJson = JsonSerializer.Serialize(request, jsonOptions);
            var resultJson = startRunSubscriber.InvokeFunc(requestJson);
            return Deserialize(resultJson, UnreadableStartResult(runCount, safeJob, safeRoute, stopAtSeriesRank25));
        }
        catch (Exception ex)
        {
            log.Debug($"[mom IPC] StartRun failed: {ex.Message}; trying legacy CC start IPC");
            return StartRunLegacyFallback(runCount, safeJob, stopAtSeriesRank25, safeRoute, ex);
        }
    }

    public MomRunResult CancelActiveRun()
        => InvokeJson(cancelSubscriber, new MomRunResult
        {
            Status = MomRunStatus.Cancelled,
            Summary = "mom cancel result unavailable.",
            FailureReason = "Cancel result unavailable.",
        }, "[mom IPC] CancelActiveRun failed");

    private MomIpcReadiness FetchReadiness(DateTime now)
    {
        try
        {
            var json = readinessSubscriber.InvokeFunc();
            var readiness = Deserialize(json, MomIpcReadiness.Unreadable());
            readiness.IpcRegistered = true;
            readiness.Normalize();
            ClearReadinessFailure();
            return readiness;
        }
        catch (Exception readinessException)
        {
            return FetchLegacyReadiness(readinessException, now);
        }
    }

    private MomIpcReadiness FetchLegacyReadiness(Exception readinessException, DateTime now)
    {
        try
        {
            var isReady = isReadySubscriber.InvokeFunc();
            ClearReadinessFailure();
            return isReady
                ? MomIpcReadiness.LegacyReady()
                : MomIpcReadiness.LegacyNotReady();
        }
        catch (Exception legacyException)
        {
            var readiness = MomIpcReadiness.FromMissing(readinessException, legacyException);
            LogReadinessFailure(readiness, now);
            return readiness;
        }
    }

    private MomRunResult StartRunLegacyFallback(
        int runCount,
        string job,
        bool stopAtSeriesRank25,
        string route,
        Exception startRunException)
    {
        if (!string.Equals(route, MomRunRoutes.CasualCc, StringComparison.OrdinalIgnoreCase))
        {
            return RejectedStartResult(
                runCount,
                job,
                route,
                stopAtSeriesRank25,
                $"mom legacy IPC only supports route '{MomRunRoutes.CasualCc}'.");
        }

        try
        {
            var json = startRunsWithOptionsSubscriber.InvokeFunc(runCount, job, stopAtSeriesRank25);
            return Deserialize(json, UnreadableStartResult(runCount, job, route, stopAtSeriesRank25));
        }
        catch (Exception optionsException)
        {
            log.Warning($"[mom IPC] StartCcRunsWithOptions failed: {optionsException.Message}; trying StartCcRuns without options");
        }

        try
        {
            var json = startRunsSubscriber.InvokeFunc(runCount, job);
            return Deserialize(json, UnreadableStartResult(runCount, job, route, stopAtSeriesRank25));
        }
        catch (Exception legacyException)
        {
            var reason = $"mom start IPC is not registered. StartRun failed: {startRunException.Message}; legacy StartCcRuns failed: {legacyException.Message}";
            log.Warning($"[mom IPC] {reason}");
            return FailedStartResult(runCount, job, route, stopAtSeriesRank25, reason);
        }
    }

    private T InvokeJson<T>(ICallGateSubscriber<string> subscriber, T fallback, string logMessage)
    {
        try
        {
            var json = subscriber.InvokeFunc();
            return Deserialize(json, fallback);
        }
        catch (Exception ex)
        {
            log.Debug($"{logMessage}: {ex.Message}");
            return fallback;
        }
    }

    private T Deserialize<T>(string json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
            return fallback;

        try
        {
            return JsonSerializer.Deserialize<T>(json, jsonOptions) ?? fallback;
        }
        catch (Exception ex)
        {
            log.Warning($"[mom IPC] Failed to deserialize payload: {ex.Message}");
            return fallback;
        }
    }

    private void LogReadinessFailure(MomIpcReadiness readiness, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(readiness.FailureReason))
            return;

        if (readiness.FailureReason == lastReadinessFailureMessage &&
            now - lastReadinessFailureLoggedAtUtc < ReadinessFailureLogThrottle)
        {
            return;
        }

        log.Debug($"[mom IPC] readiness failed: {readiness.FailureReason}");
        lastReadinessFailureMessage = readiness.FailureReason;
        lastReadinessFailureLoggedAtUtc = now;
    }

    private void ClearReadinessFailure()
    {
        lastReadinessFailureMessage = string.Empty;
        lastReadinessFailureLoggedAtUtc = DateTime.MinValue;
    }

    private static MomRunResult UnreadableStartResult(int runCount, string job, string route, bool stopAtSeriesRank25)
        => FailedStartResult(runCount, job, route, stopAtSeriesRank25, "mom returned an unreadable start result.");

    private static MomRunResult FailedStartResult(int runCount, string job, string route, bool stopAtSeriesRank25, string reason)
        => new()
        {
            Status = MomRunStatus.Failed,
            Route = route ?? string.Empty,
            RequestedRunCount = runCount,
            RequestedJob = job ?? string.Empty,
            StopAtSeriesRank25 = stopAtSeriesRank25,
            Summary = reason,
            FailureReason = reason,
        };

    private static MomRunResult RejectedStartResult(int runCount, string job, string route, bool stopAtSeriesRank25, string reason)
        => new()
        {
            Status = MomRunStatus.Rejected,
            Route = route ?? string.Empty,
            RequestedRunCount = runCount,
            RequestedJob = job ?? string.Empty,
            StopAtSeriesRank25 = stopAtSeriesRank25,
            Summary = reason,
            FailureReason = reason,
        };
}

public sealed class MomIpcReadiness
{
    public bool IpcReady { get; set; }
    public bool CanStart { get; set; }
    public bool PluginEnabled { get; set; }
    public bool LoggedIn { get; set; }
    public bool LocalPlayerAvailable { get; set; }
    public bool BetweenAreas { get; set; }
    public bool DtrReady { get; set; }
    public string StartupSummary { get; set; } = string.Empty;
    public string BlockReason { get; set; } = string.Empty;
    public string[] SupportedRoutes { get; set; } = [];
    public bool IpcRegistered { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;

    public bool IsReady => CanStart;

    public void Normalize()
    {
        if (IpcReady)
            IpcRegistered = true;

        if (SupportedRoutes.Length == 0 && IpcRegistered)
            SupportedRoutes = [MomRunRoutes.CasualCc];

        if (string.IsNullOrWhiteSpace(Summary))
        {
            Summary = CanStart
                ? !string.IsNullOrWhiteSpace(StartupSummary) && !StartupSummary.Equals("Startup stable", StringComparison.OrdinalIgnoreCase)
                    ? StartupSummary
                    : "Ready"
                : !string.IsNullOrWhiteSpace(BlockReason)
                    ? BlockReason
                    : !string.IsNullOrWhiteSpace(StartupSummary)
                        ? StartupSummary
                        : "mom IPC loaded but not ready.";
        }

        if (!CanStart && string.IsNullOrWhiteSpace(BlockReason))
            BlockReason = Summary;
    }

    public static MomIpcReadiness NotChecked()
        => new()
        {
            IpcRegistered = false,
            IpcReady = false,
            CanStart = false,
            Summary = "mom IPC has not been checked yet.",
        };

    public static MomIpcReadiness Unreadable()
        => new()
        {
            IpcRegistered = true,
            IpcReady = true,
            CanStart = false,
            Summary = "mom.GetReadiness returned an unreadable payload.",
            BlockReason = "Unreadable readiness payload.",
        };

    public static MomIpcReadiness LegacyReady()
        => new()
        {
            IpcRegistered = true,
            IpcReady = true,
            CanStart = true,
            PluginEnabled = true,
            Summary = "Ready (legacy IPC)",
            SupportedRoutes = [MomRunRoutes.CasualCc],
        };

    public static MomIpcReadiness LegacyNotReady()
        => new()
        {
            IpcRegistered = true,
            IpcReady = true,
            CanStart = false,
            Summary = "mom legacy IPC is loaded but not ready.",
            BlockReason = "mom legacy IPC is loaded but not ready.",
            SupportedRoutes = [MomRunRoutes.CasualCc],
        };

    public static MomIpcReadiness FromMissing(Exception readinessException, Exception legacyException)
    {
        var reason = $"mom.GetReadiness failed: {readinessException.Message}; mom.IsReady failed: {legacyException.Message}";
        return new MomIpcReadiness
        {
            IpcRegistered = false,
            IpcReady = false,
            CanStart = false,
            Summary = "mom IPC is not registered. Load or enable mom.",
            FailureReason = reason,
        };
    }
}

internal sealed class MomStartRunRequest
{
    public string Route { get; set; } = MomRunRoutes.CasualCc;
    public int RunCount { get; set; }
    public string Job { get; set; } = string.Empty;
    public bool StopAtSeriesRank25 { get; set; }
    public string RequestedBy { get; set; } = "VERMAXION";
    public bool EnableAutomation { get; set; } = true;
}
