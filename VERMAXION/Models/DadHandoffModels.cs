using System;

namespace VERMAXION.Models;

public static class DadHandoffContract
{
    public const int Version = 2;
    public const int LeaseSeconds = 15;
    public const string ReserveChannel = "VERMAXION.ReserveDadHandoffV2Json";
    public const string ReleaseChannel = "VERMAXION.ReleaseDadHandoffV2Json";
    public const string GrantedChannel = "VERMAXION.DadHandoffGrantedV2Json";
}

public enum DadHandoffReservationState
{
    Pending = 0,
    Granting = 1,
    Granted = 2,
    Released = 3,
    Rejected = 4,
}

public sealed class DadHandoffReservationRequest
{
    public int Version { get; set; } = DadHandoffContract.Version;
    public string OperationToken { get; set; } = string.Empty;
    public string SchedulerRunId { get; set; } = string.Empty;
    public string SlotId { get; set; } = string.Empty;
    public string AccountKey { get; set; } = string.Empty;
    public string CharacterKey { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public int LeaseSeconds { get; set; } = DadHandoffContract.LeaseSeconds;
}

public sealed class DadHandoffReservationStatus
{
    public int Version { get; set; } = DadHandoffContract.Version;
    public string OperationToken { get; set; } = string.Empty;
    public string SchedulerRunId { get; set; } = string.Empty;
    public string SlotId { get; set; } = string.Empty;
    public string AccountKey { get; set; } = string.Empty;
    public string CharacterKey { get; set; } = string.Empty;
    public DadHandoffReservationState State { get; set; } = DadHandoffReservationState.Released;
    public string VermaxionActivity { get; set; } = string.Empty;
    public string VermaxionState { get; set; } = string.Empty;
    public bool AutoRetainerBusyKnown { get; set; }
    public bool AutoRetainerBusy { get; set; }
    public bool MultiModeKnown { get; set; }
    public bool MultiModeEnabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public string Summary { get; set; } = string.Empty;

    public bool BlocksNewWork => State is DadHandoffReservationState.Pending
        or DadHandoffReservationState.Granting
        or DadHandoffReservationState.Granted;
}

public sealed class DadHandoffObservation
{
    public bool VermaxionBusy { get; init; }
    public string VermaxionActivity { get; init; } = string.Empty;
    public string VermaxionState { get; init; } = string.Empty;
    public bool AutoRetainerBusyKnown { get; init; }
    public bool AutoRetainerBusy { get; init; }
    public bool MultiModeKnown { get; init; }
    public bool MultiModeEnabled { get; init; }
    public bool VermaxionSuppressionOwned { get; init; }
}

public sealed class DadHandoffReservationMachine
{
    private DadHandoffReservationRequest? request;
    private DadHandoffReservationState state = DadHandoffReservationState.Released;
    private DateTime createdAtUtc;
    private DateTime updatedAtUtc;
    private DateTime? leaseExpiresUtc;
    private string summary = "No DAD handoff reservation.";

    public bool BlocksNewWork => state is DadHandoffReservationState.Pending
        or DadHandoffReservationState.Granting
        or DadHandoffReservationState.Granted;

    public string OperationToken => request?.OperationToken ?? string.Empty;

    public DadHandoffReservationStatus Reserve(
        DadHandoffReservationRequest candidate,
        DadHandoffObservation observation,
        DateTime nowUtc)
    {
        nowUtc = EnsureUtc(nowUtc);
        Normalize(candidate);
        var validation = Validate(candidate);
        if (!string.IsNullOrWhiteSpace(validation))
            return Rejected(candidate, observation, nowUtc, validation);

        if (BlocksNewWork && request != null &&
            !string.Equals(request.OperationToken, candidate.OperationToken, StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(candidate, observation, nowUtc,
                $"VERMAXION already holds DAD reservation {request.OperationToken}.");
        }

        if (request == null || !string.Equals(request.OperationToken, candidate.OperationToken, StringComparison.OrdinalIgnoreCase))
        {
            request = Clone(candidate);
            createdAtUtc = nowUtc;
            state = observation.VermaxionBusy
                ? DadHandoffReservationState.Pending
                : DadHandoffReservationState.Granting;
            summary = observation.VermaxionBusy
                ? "VERMAXION is finishing its current work before DAD takeover."
                : "VERMAXION is disabling and draining AutoRetainer for DAD takeover.";
        }

        updatedAtUtc = nowUtc;
        leaseExpiresUtc = nowUtc.AddSeconds(DadHandoffContract.LeaseSeconds);
        return Snapshot(observation);
    }

    public DadHandoffReservationStatus Observe(
        DadHandoffObservation observation,
        bool safeToGrant,
        DateTime nowUtc)
    {
        nowUtc = EnsureUtc(nowUtc);
        if (!BlocksNewWork || request == null)
            return Snapshot(observation);

        if (leaseExpiresUtc.HasValue && nowUtc >= leaseExpiresUtc.Value)
        {
            state = DadHandoffReservationState.Released;
            updatedAtUtc = nowUtc;
            summary = "DAD handoff reservation lease expired; VERMAXION may resume new work.";
            leaseExpiresUtc = null;
            return Snapshot(observation);
        }

        if (state == DadHandoffReservationState.Pending && !observation.VermaxionBusy)
        {
            state = DadHandoffReservationState.Granting;
            updatedAtUtc = nowUtc;
            summary = "VERMAXION finished current work and is disabling/draining AutoRetainer.";
        }

        if (state == DadHandoffReservationState.Granting && safeToGrant)
        {
            state = DadHandoffReservationState.Granted;
            updatedAtUtc = nowUtc;
            summary = "DAD handoff granted: VERMAXION idle, Multi Mode off, AutoRetainer idle.";
        }

        return Snapshot(observation);
    }

    public DadHandoffReservationStatus Release(string operationToken, DadHandoffObservation observation, DateTime nowUtc)
    {
        nowUtc = EnsureUtc(nowUtc);
        operationToken = operationToken?.Trim() ?? string.Empty;
        if (request == null || !string.Equals(request.OperationToken, operationToken, StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(
                new DadHandoffReservationRequest { OperationToken = operationToken },
                observation,
                nowUtc,
                "DAD handoff release token does not own the active reservation.");
        }

        state = DadHandoffReservationState.Released;
        updatedAtUtc = nowUtc;
        leaseExpiresUtc = null;
        summary = "DAD released the VERMAXION handoff reservation.";
        return Snapshot(observation);
    }

    public DadHandoffReservationStatus Snapshot(DadHandoffObservation observation)
        => new()
        {
            OperationToken = request?.OperationToken ?? string.Empty,
            SchedulerRunId = request?.SchedulerRunId ?? string.Empty,
            SlotId = request?.SlotId ?? string.Empty,
            AccountKey = request?.AccountKey ?? string.Empty,
            CharacterKey = request?.CharacterKey ?? string.Empty,
            State = state,
            VermaxionActivity = observation.VermaxionActivity,
            VermaxionState = observation.VermaxionState,
            AutoRetainerBusyKnown = observation.AutoRetainerBusyKnown,
            AutoRetainerBusy = observation.AutoRetainerBusy,
            MultiModeKnown = observation.MultiModeKnown,
            MultiModeEnabled = observation.MultiModeEnabled,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
            LeaseExpiresUtc = leaseExpiresUtc,
            Summary = summary,
        };

    private DadHandoffReservationStatus Rejected(
        DadHandoffReservationRequest candidate,
        DadHandoffObservation observation,
        DateTime nowUtc,
        string reason)
        => new()
        {
            OperationToken = candidate.OperationToken,
            SchedulerRunId = candidate.SchedulerRunId,
            SlotId = candidate.SlotId,
            AccountKey = candidate.AccountKey,
            CharacterKey = candidate.CharacterKey,
            State = DadHandoffReservationState.Rejected,
            VermaxionActivity = observation.VermaxionActivity,
            VermaxionState = observation.VermaxionState,
            AutoRetainerBusyKnown = observation.AutoRetainerBusyKnown,
            AutoRetainerBusy = observation.AutoRetainerBusy,
            MultiModeKnown = observation.MultiModeKnown,
            MultiModeEnabled = observation.MultiModeEnabled,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Summary = reason,
        };

    private static string Validate(DadHandoffReservationRequest candidate)
    {
        if (candidate.Version != DadHandoffContract.Version)
            return $"Unsupported DAD handoff contract version {candidate.Version}.";
        if (string.IsNullOrWhiteSpace(candidate.OperationToken))
            return "DAD handoff reservation requires an operation token.";
        if (string.IsNullOrWhiteSpace(candidate.AccountKey) || string.IsNullOrWhiteSpace(candidate.CharacterKey))
            return "DAD handoff reservation requires an account and character target.";
        return string.Empty;
    }

    private static void Normalize(DadHandoffReservationRequest candidate)
    {
        candidate.OperationToken = candidate.OperationToken?.Trim() ?? string.Empty;
        candidate.SchedulerRunId = candidate.SchedulerRunId?.Trim() ?? string.Empty;
        candidate.SlotId = candidate.SlotId?.Trim() ?? string.Empty;
        candidate.AccountKey = candidate.AccountKey?.Trim() ?? string.Empty;
        candidate.CharacterKey = candidate.CharacterKey?.Trim() ?? string.Empty;
        candidate.LeaseSeconds = DadHandoffContract.LeaseSeconds;
    }

    private static DadHandoffReservationRequest Clone(DadHandoffReservationRequest source)
        => new()
        {
            Version = source.Version,
            OperationToken = source.OperationToken,
            SchedulerRunId = source.SchedulerRunId,
            SlotId = source.SlotId,
            AccountKey = source.AccountKey,
            CharacterKey = source.CharacterKey,
            RequestedAtUtc = source.RequestedAtUtc,
            LeaseSeconds = DadHandoffContract.LeaseSeconds,
        };

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
