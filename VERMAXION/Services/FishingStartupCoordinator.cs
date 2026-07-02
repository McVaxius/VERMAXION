using System;
using VERMAXION.Models;

namespace VERMAXION.Services;

public enum FishingStartupTrigger
{
    Clock,
    AutoRetainerPostprocess,
    Manual,
}

public enum FishingStartupAction
{
    None,
    Waiting,
    AlreadyHandled,
    RelogStarted,
    FishingStarted,
}

public sealed record FishingStartupResult(
    FishingStartupTrigger Trigger,
    FishingStartupAction Action,
    bool WindowActive,
    DateTimeOffset? RegistrationStartUtc,
    FishingSelectionResult Selection,
    string Reason)
{
    public bool ClaimsStartup => WindowActive && Selection.Selected;
    public bool Started => Action is FishingStartupAction.RelogStarted or FishingStartupAction.FishingStarted;
}

public static class FishingStartupDiagnostics
{
    public static string FormatStarted(FishingStartupResult result)
        => $"[Fishing][Startup] trigger={result.Trigger}, registration={result.RegistrationStartUtc:u}, " +
           $"target={result.Selection.CharacterKey}, action={result.Action}, reason={result.Reason}";
}

public interface IFishingStartupRuntime
{
    int PreWindowOffsetMinutes { get; }
    bool CanInitiateStartup { get; }
    bool IsFishingActive { get; }
    bool IsRelogActive { get; }

    FishingSelectionResult SelectTarget();
    int DisableAlwaysFishOnOtherCharacters(string selectedCharacterKey);
    bool RequestRelog(string characterKey);
    bool StartFishing();
}

/// <summary>
/// Coordinates all Ocean Fishing startup triggers and de-duplicates actions by
/// registration window. The relog attempt and fishing-start attempt are tracked
/// separately so a relogged character can start fishing in the same window.
/// </summary>
public sealed class FishingStartupCoordinator
{
    private readonly IFishingStartupRuntime runtime;

    private DateTimeOffset? trackedRegistrationStartUtc;
    private bool relogAttempted;
    private bool fishingStartAttempted;

    public FishingStartupCoordinator(IFishingStartupRuntime runtime)
    {
        this.runtime = runtime;
    }

    public FishingStartupResult Poll(DateTimeOffset nowUtc, FishingStartupTrigger trigger)
    {
        if (!OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                nowUtc,
                runtime.PreWindowOffsetMinutes,
                out var window))
        {
            return None(
                trigger,
                OceanFishingSchedulePolicy.DescribeInactiveStartupWindow(
                    nowUtc,
                    runtime.PreWindowOffsetMinutes));
        }

        TrackWindow(window.RegistrationStartUtc);

        var selection = runtime.SelectTarget();
        if (!selection.Selected)
        {
            return new FishingStartupResult(
                trigger,
                FishingStartupAction.None,
                WindowActive: true,
                window.RegistrationStartUtc,
                selection,
                selection.Reason);
        }

        if (selection.RequiresRelog)
            return TryStartRelog(trigger, window, selection);

        return TryStartFishing(trigger, window, selection);
    }

    public void SuppressCurrentWindow(DateTimeOffset nowUtc)
    {
        if (!OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                nowUtc,
                runtime.PreWindowOffsetMinutes,
                out var window))
        {
            return;
        }

        TrackWindow(window.RegistrationStartUtc);
        relogAttempted = true;
        fishingStartAttempted = true;
    }

    private FishingStartupResult TryStartRelog(
        FishingStartupTrigger trigger,
        OceanFishingStartupWindow window,
        FishingSelectionResult selection)
    {
        if (relogAttempted)
            return Result(trigger, FishingStartupAction.AlreadyHandled, window, selection,
                $"Relog was already attempted for the {window.RegistrationStartUtc:u} registration window.");

        if (!runtime.CanInitiateStartup)
            return Result(trigger, FishingStartupAction.Waiting, window, selection,
                "Waiting for the current character and VERMAXION engine to become ready.");

        if (runtime.IsRelogActive)
            return Result(trigger, FishingStartupAction.Waiting, window, selection,
                "A fishing relog sequence is already active.");

        runtime.DisableAlwaysFishOnOtherCharacters(selection.CharacterKey);
        if (!runtime.RequestRelog(selection.CharacterKey))
            return Result(trigger, FishingStartupAction.Waiting, window, selection,
                $"Could not start relog to {selection.CharacterKey}; polling will retry.");

        relogAttempted = true;
        return Result(trigger, FishingStartupAction.RelogStarted, window, selection,
            $"Selected {selection.CharacterKey} (Fisher {selection.FisherLevel}) and started relog.");
    }

    private FishingStartupResult TryStartFishing(
        FishingStartupTrigger trigger,
        OceanFishingStartupWindow window,
        FishingSelectionResult selection)
    {
        if (fishingStartAttempted)
            return Result(trigger, FishingStartupAction.AlreadyHandled, window, selection,
                $"Fishing prep was already attempted for the {window.RegistrationStartUtc:u} registration window.");

        if (runtime.IsFishingActive)
        {
            fishingStartAttempted = true;
            return Result(trigger, FishingStartupAction.AlreadyHandled, window, selection,
                "Fishing prep is already active.");
        }

        if (!runtime.CanInitiateStartup)
            return Result(trigger, FishingStartupAction.Waiting, window, selection,
                "Waiting for the current character and VERMAXION engine to become ready.");

        if (runtime.IsRelogActive)
            return Result(trigger, FishingStartupAction.Waiting, window, selection,
                "Waiting for the fishing relog sequence to finish.");

        runtime.DisableAlwaysFishOnOtherCharacters(selection.CharacterKey);
        if (!runtime.StartFishing())
            return Result(trigger, FishingStartupAction.Waiting, window, selection,
                "Could not start fishing prep; polling will retry.");

        fishingStartAttempted = true;
        return Result(trigger, FishingStartupAction.FishingStarted, window, selection,
            $"Selected current character {selection.CharacterKey} (Fisher {selection.FisherLevel}) and started fishing prep.");
    }

    private void TrackWindow(DateTimeOffset registrationStartUtc)
    {
        if (trackedRegistrationStartUtc == registrationStartUtc)
            return;

        trackedRegistrationStartUtc = registrationStartUtc;
        relogAttempted = false;
        fishingStartAttempted = false;
    }

    private static FishingStartupResult None(FishingStartupTrigger trigger, string reason)
        => new(
            trigger,
            FishingStartupAction.None,
            WindowActive: false,
            RegistrationStartUtc: null,
            FishingSelectionResult.None(reason),
            reason);

    private static FishingStartupResult Result(
        FishingStartupTrigger trigger,
        FishingStartupAction action,
        OceanFishingStartupWindow window,
        FishingSelectionResult selection,
        string reason)
        => new(
            trigger,
            action,
            WindowActive: true,
            window.RegistrationStartUtc,
            selection,
            reason);
}
