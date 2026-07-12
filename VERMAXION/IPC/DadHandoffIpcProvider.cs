using System;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class DadHandoffIpcProvider : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPluginLog log;
    private readonly AutoRetainerIPC autoRetainer;
    private readonly Func<AutomationStatus> getAutomationStatus;
    private readonly DadHandoffReservationMachine machine = new();
    private readonly ICallGateProvider<string, string> reserveProvider;
    private readonly ICallGateProvider<string, string> releaseProvider;
    private readonly ICallGateProvider<string, object> grantedProvider;
    private readonly object gate = new();
    private DadHandoffReservationState lastLoggedState = DadHandoffReservationState.Released;
    private DateTime nextDrainCheckUtc = DateTime.MinValue;
    private DateTime nextObservationUtc = DateTime.MinValue;
    private bool disposed;

    public DadHandoffIpcProvider(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        AutoRetainerIPC autoRetainer,
        Func<AutomationStatus> getAutomationStatus)
    {
        this.log = log;
        this.autoRetainer = autoRetainer;
        this.getAutomationStatus = getAutomationStatus;
        reserveProvider = pluginInterface.GetIpcProvider<string, string>(DadHandoffContract.ReserveChannel);
        releaseProvider = pluginInterface.GetIpcProvider<string, string>(DadHandoffContract.ReleaseChannel);
        grantedProvider = pluginInterface.GetIpcProvider<string, object>(DadHandoffContract.GrantedChannel);
        reserveProvider.RegisterFunc(ReserveJson);
        releaseProvider.RegisterFunc(ReleaseJson);
    }

    public bool BlocksNewWork
    {
        get
        {
            lock (gate)
                return machine.BlocksNewWork;
        }
    }

    public DadHandoffReservationStatus CurrentStatus
    {
        get
        {
            lock (gate)
                return machine.Snapshot(ObserveRuntime());
        }
    }

    public void Update()
    {
        lock (gate)
        {
            if (disposed)
                return;

            var now = DateTime.UtcNow;
            if (!machine.BlocksNewWork || now < nextObservationUtc)
                return;
            nextObservationUtc = now.AddMilliseconds(100);
            var observation = ObserveRuntime();
            var before = machine.Snapshot(observation).State;
            var safeToGrant = false;
            if (before == DadHandoffReservationState.Granting && now >= nextDrainCheckUtc)
            {
                nextDrainCheckUtc = now.AddMilliseconds(100);
                safeToGrant = TryReachSafeGrantState(ref observation);
            }

            var status = machine.Observe(observation, safeToGrant, now);
            if (status.State == DadHandoffReservationState.Granting && before == DadHandoffReservationState.Pending)
            {
                safeToGrant = TryReachSafeGrantState(ref observation);
                status = machine.Observe(observation, safeToGrant, now);
            }

            LogTransition(status);
            if (before != DadHandoffReservationState.Granted && status.State == DadHandoffReservationState.Granted)
            {
                try
                {
                    grantedProvider.SendMessage(JsonSerializer.Serialize(status, JsonOptions));
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "[DAD handoff] Failed to publish the local grant event.");
                }
            }
        }
    }

    private string ReserveJson(string json)
    {
        lock (gate)
        {
            DadHandoffReservationStatus status;
            try
            {
                var request = JsonSerializer.Deserialize<DadHandoffReservationRequest>(json ?? string.Empty, JsonOptions)
                              ?? new DadHandoffReservationRequest();
                status = machine.Reserve(request, ObserveRuntime(), DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                status = new DadHandoffReservationStatus
                {
                    State = DadHandoffReservationState.Rejected,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Summary = $"Malformed DAD handoff reservation: {ex.Message}",
                };
            }

            LogTransition(status);
            return JsonSerializer.Serialize(status, JsonOptions);
        }
    }

    private string ReleaseJson(string operationToken)
    {
        lock (gate)
        {
            var status = machine.Release(operationToken, ObserveRuntime(), DateTime.UtcNow);
            LogTransition(status);
            return JsonSerializer.Serialize(status, JsonOptions);
        }
    }

    private bool TryReachSafeGrantState(ref DadHandoffObservation observation)
    {
        if (observation.VermaxionBusy)
            return false;

        if (observation.VermaxionSuppressionOwned && !autoRetainer.ReleaseSuppressionIfOwned())
            return false;

        var multiMode = autoRetainer.ReadMultiModeEnabled();
        if (!multiMode.Success)
        {
            observation = ObserveRuntime();
            return false;
        }

        if (multiMode.Enabled && !autoRetainer.TrySetMultiModeEnabled(false, out _))
        {
            observation = ObserveRuntime();
            return false;
        }

        var busy = autoRetainer.ReadBusyState();
        observation = ObserveRuntime(multiModeOverride: false, busyOverride: busy);
        return busy.Success && !busy.Busy &&
               !autoRetainer.SuppressionOwnedByVermaxion &&
               observation.MultiModeKnown && !observation.MultiModeEnabled;
    }

    private DadHandoffObservation ObserveRuntime(
        bool? multiModeOverride = null,
        PluginBusyReadResult? busyOverride = null)
    {
        var automation = getAutomationStatus();
        var multiMode = autoRetainer.ReadMultiModeEnabled();
        var busy = busyOverride ?? autoRetainer.ReadBusyState();
        return new DadHandoffObservation
        {
            VermaxionBusy = automation.IsBusy,
            VermaxionActivity = automation.Activity,
            VermaxionState = automation.State,
            AutoRetainerBusyKnown = busy.Success,
            AutoRetainerBusy = busy.Busy,
            MultiModeKnown = multiModeOverride.HasValue || multiMode.Success,
            MultiModeEnabled = multiModeOverride ?? multiMode.Enabled,
            VermaxionSuppressionOwned = autoRetainer.SuppressionOwnedByVermaxion,
        };
    }

    private void LogTransition(DadHandoffReservationStatus status)
    {
        if (status.State == lastLoggedState)
            return;
        lastLoggedState = status.State;
        log.Information(
            "[DAD handoff] Reservation {OperationToken} transitioned to {State}: {Summary}",
            status.OperationToken,
            status.State,
            status.Summary);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            reserveProvider.UnregisterFunc();
            releaseProvider.UnregisterFunc();
        }
    }
}
