using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.IPC;

public sealed class DadHandoffIpcProvider : IDisposable
{
    private readonly IPluginLog log;
    private readonly AutoRetainerIPC autoRetainer;
    private readonly Func<AutomationStatus> getAutomationStatus;
    private readonly Action yieldUnstartedBeforeArGate;
    private readonly DadHandoffReservationMachine machine = new();
    private readonly DadHandoffPreGrantMultiModeLease multiModeLease = new();
    private readonly ICallGateProvider<string, string> reserveProvider;
    private readonly ICallGateProvider<string, string> releaseProvider;
    private readonly ICallGateProvider<string, object> grantedProvider;
    private readonly object gate = new();
    private DadHandoffReservationState lastLoggedState = DadHandoffReservationState.Released;
    private DateTime nextDrainCheckUtc = DateTime.MinValue;
    private DateTime nextObservationUtc = DateTime.MinValue;
    private DateTime nextMultiModeRestoreUtc = DateTime.MinValue;
    private bool disposed;

    public DadHandoffIpcProvider(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        AutoRetainerIPC autoRetainer,
        Func<AutomationStatus> getAutomationStatus,
        Action yieldUnstartedBeforeArGate)
    {
        this.log = log;
        this.autoRetainer = autoRetainer;
        this.getAutomationStatus = getAutomationStatus;
        this.yieldUnstartedBeforeArGate = yieldUnstartedBeforeArGate;
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
            TryRestorePreGrantMultiMode(now);
            if (multiModeLease.RestorePending)
                return;
            if (!machine.BlocksNewWork || now < nextObservationUtc)
                return;

            if (machine.IsLeaseExpired(now))
            {
                var beforeExpiry = machine.State;
                var expired = machine.Observe(ObserveRuntime(), safeToGrant: false, now);
                HandleAttemptTransition(beforeExpiry, expired.State, now);
                LogTransition(expired);
                return;
            }

            yieldUnstartedBeforeArGate();
            nextObservationUtc = now.AddMilliseconds(100);
            var observation = ObserveRuntime();
            var before = machine.State;
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

            HandleAttemptTransition(before, status.State, now);

            LogTransition(status);
            if (before != DadHandoffReservationState.Granted && status.State == DadHandoffReservationState.Granted)
            {
                try
                {
                    grantedProvider.SendMessage(DadHandoffJson.SerializeStatus(status));
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
                TryRestorePreGrantMultiMode(DateTime.UtcNow);
                var request = DadHandoffJson.DeserializeRequest(json);
                status = machine.Reserve(request, ObserveRuntime(), DateTime.UtcNow);
                if (status.BlocksNewWork)
                    yieldUnstartedBeforeArGate();
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
            return DadHandoffJson.SerializeStatus(status);
        }
    }

    private string ReleaseJson(string operationToken)
    {
        lock (gate)
        {
            var before = machine.State;
            var status = machine.Release(operationToken, ObserveRuntime(), DateTime.UtcNow);
            if (status.State == DadHandoffReservationState.Released)
            {
                HandleAttemptTransition(before, status.State, DateTime.UtcNow);
                status = machine.Snapshot(ObserveRuntime());
            }
            LogTransition(status);
            return DadHandoffJson.SerializeStatus(status);
        }
    }

    private bool TryReachSafeGrantState(ref DadHandoffObservation observation)
    {
        for (var step = 0; step < 3; step++)
        {
            switch (DadHandoffGrantPolicy.Decide(observation))
            {
                case DadHandoffGrantAction.ReleaseVermaxionSuppression:
                    if (!autoRetainer.ReleaseSuppressionIfOwned())
                        return false;
                    observation = ObserveRuntime();
                    continue;
                case DadHandoffGrantAction.DisableMultiMode:
                    if (!autoRetainer.TrySetMultiModeEnabled(false, out _))
                    {
                        observation = ObserveRuntime();
                        return false;
                    }

                    multiModeLease.RecordDisabledByVermaxion();
                    observation = ObserveRuntime(multiModeOverride: false);
                    continue;
                case DadHandoffGrantAction.Grant:
                    return true;
                default:
                    return false;
            }
        }

        return DadHandoffGrantPolicy.Decide(observation) == DadHandoffGrantAction.Grant;
    }

    private void HandleAttemptTransition(
        DadHandoffReservationState before,
        DadHandoffReservationState after,
        DateTime nowUtc)
    {
        if (after == DadHandoffReservationState.Granted && before != DadHandoffReservationState.Granted)
        {
            multiModeLease.CompleteGrant();
            return;
        }

        if (after == DadHandoffReservationState.Released &&
            before is DadHandoffReservationState.Pending or DadHandoffReservationState.Granting)
        {
            multiModeLease.EndWithoutGrant();
            TryRestorePreGrantMultiMode(nowUtc, force: true);
        }
    }

    private bool TryRestorePreGrantMultiMode(DateTime nowUtc, bool force = false)
    {
        if (!multiModeLease.RestorePending)
            return true;
        if (!force && nowUtc < nextMultiModeRestoreUtc)
            return false;

        nextMultiModeRestoreUtc = nowUtc.AddSeconds(2);
        if (!autoRetainer.TrySetMultiModeEnabled(true, out var error))
        {
            log.Warning(
                "[DAD handoff] Waiting to restore AutoRetainer Multi Mode after an ungranted reservation: {Error}",
                error);
            return false;
        }

        multiModeLease.RecordRestored();
        nextMultiModeRestoreUtc = DateTime.MinValue;
        log.Information("[DAD handoff] Restored AutoRetainer Multi Mode changed by the ungranted reservation.");
        return true;
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
            if (machine.State is DadHandoffReservationState.Pending or DadHandoffReservationState.Granting)
                multiModeLease.EndWithoutGrant();
            else if (machine.State == DadHandoffReservationState.Granted)
                multiModeLease.CompleteGrant();
            TryRestorePreGrantMultiMode(DateTime.UtcNow, force: true);
            reserveProvider.UnregisterFunc();
            releaseProvider.UnregisterFunc();
        }
    }
}
