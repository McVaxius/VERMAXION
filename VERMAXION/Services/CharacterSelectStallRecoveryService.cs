using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class CharacterSelectStallRecoveryService
{
    private const string CharacterSelectAddonName = "CharaSelect";
    private const string CharacterSelectListMenuAddonName = "_CharaSelectListMenu";

    private readonly IPluginLog log;
    private readonly CharacterSelectStallRecoveryState state = new();
    private bool manualAttemptQueued;
    private bool confirmationClickPending;

    public CharacterSelectStallRecoveryService(IPluginLog log)
    {
        this.log = log;
    }

    public string StatusText { get; private set; } = "Idle";
    public string LastBlockedReason { get; private set; } = string.Empty;
    public bool IsStallActive => state.StallActive;
    public bool IsAutomaticAttemptIssued => state.AutomaticAttemptIssued;
    public bool IsAwaitingLoginConfirmation => state.AwaitingLoginConfirmation;

    internal CharacterSelectRecoveryEligibility GetEligibility(bool recoveryEnabled)
        => CharacterSelectRecoveryPolicy.Evaluate(ReadSafetySnapshot(recoveryEnabled));

    internal CharacterSelectRecoveryEligibility GetIntentionalFishingWakeEligibility()
        => CharacterSelectRecoveryPolicy.Evaluate(ReadSafetySnapshot(recoveryEnabled: true));

    public bool TryRequestIntentionalFishingWake(out string error)
    {
        var succeeded = ExecuteAttempt(recoveryEnabled: true, "scheduled fishing wake");
        error = succeeded ? string.Empty : LastBlockedReason;
        return succeeded;
    }

    public void CompleteIntentionalFishingWake()
    {
        Reset();
        log.Information("[CharacterSelectRecovery] Scheduled fishing wake handed off; ordinary recovery state reset for any candidate relog.");
    }

    public void QueueManualAttempt()
    {
        if (manualAttemptQueued)
            return;

        manualAttemptQueued = true;
        StatusText = "Manual first-character recovery queued.";
    }

    public void Update(
        DateTime nowUtc,
        bool recoveryEnabled,
        bool automaticRecoveryEnabled,
        bool charaSelectVisible,
        bool loggedIn)
    {
        automaticRecoveryEnabled &= recoveryEnabled;
        state.UpdateStall(nowUtc, automaticRecoveryEnabled, charaSelectVisible);
        state.ResetAttemptHistoryWhenLoggedOutAndIdle(loggedIn);

        if (state.TryConsumeAutomaticExpiry(nowUtc, automaticRecoveryEnabled))
            ExecuteAttempt(automaticRecoveryEnabled, "automatic");

        if (manualAttemptQueued)
        {
            manualAttemptQueued = false;
            ExecuteAttempt(recoveryEnabled, "manual");
        }

        if (confirmationClickPending && GameHelpers.ClickYesIfVisible())
            confirmationClickPending = false;

        if (state.AwaitingLoginConfirmation)
            StatusText = state.AutomaticAttemptIssued
                ? "Automatic attempt used"
                : confirmationClickPending
                    ? "Character selected; waiting for the OK confirmation."
                    : "Recovery request sent; waiting for one new login confirmation.";
        else if (state.StallActive && state.ArmedAtUtc != DateTime.MinValue)
        {
            StatusText = state.GetAutomaticRecoveryStatusText(nowUtc);
        }
        else if (!recoveryEnabled)
        {
            StatusText = "Disabled by global setting.";
        }
        else if (!automaticRecoveryEnabled)
        {
            StatusText = "Automatic recovery disabled by the VERMAXION master switch.";
        }
        else if (state.LoginConfirmationAccepted)
        {
            StatusText = "Recovery received one login confirmation.";
        }
        else if (!manualAttemptQueued)
        {
            StatusText = "Idle";
        }
    }

    public void NotifyLoginConfirmation()
    {
        if (!state.TryAcceptLoginConfirmation())
            return;

        LastBlockedReason = string.Empty;
        confirmationClickPending = false;
        StatusText = "Recovery received one login confirmation.";
        log.Information("[CharacterSelectRecovery] Accepted one login confirmation for the recovery request.");
    }

    public void Reset()
    {
        manualAttemptQueued = false;
        confirmationClickPending = false;
        LastBlockedReason = string.Empty;
        StatusText = "Idle";
        state.Reset();
    }

    private bool ExecuteAttempt(bool recoveryEnabled, string source)
    {
        var eligibility = GetEligibility(recoveryEnabled);
        if (!eligibility.CanAttempt)
        {
            LastBlockedReason = eligibility.Reason;
            StatusText = $"{source} recovery blocked: {eligibility.Reason}";
            log.Information($"[CharacterSelectRecovery] {source} attempt blocked: {eligibility.Reason}");
            return false;
        }

  if (!GameHelpers.TryFireAddonCallback(
          CharacterSelectListMenuAddonName,
          updateState: true,
          CharacterSelectRecoveryPolicy.ConfirmFirstCharacterCallback,
          CharacterSelectRecoveryPolicy.FirstCharacterIndex) ||
      !GameHelpers.TryFireAddonCallback(
          CharacterSelectListMenuAddonName,
          updateState: true,
          CharacterSelectRecoveryPolicy.SelectFirstCharacterCallback,
          CharacterSelectRecoveryPolicy.FirstCharacterIndex,
          CharacterSelectRecoveryPolicy.FirstCharacterIndex) ||
      !GameHelpers.TryFireAddonCallback(
          CharacterSelectListMenuAddonName,
          updateState: true,
          CharacterSelectRecoveryPolicy.ConfirmFirstCharacterCallback,
          CharacterSelectRecoveryPolicy.FirstCharacterIndex))
        {
            LastBlockedReason = "Character-select callback sequence for entry 0 was not accepted.";
            StatusText = $"{source} recovery blocked: {LastBlockedReason}";
            log.Warning($"[CharacterSelectRecovery] {source} attempt failed: {LastBlockedReason}");
            return false;
        }

        if (!state.TryBeginLoginConfirmation(eligibility))
        {
            LastBlockedReason = "A recovery login confirmation is already pending.";
            StatusText = $"{source} recovery blocked: {LastBlockedReason}";
            return false;
        }

        LastBlockedReason = string.Empty;
        confirmationClickPending = true;
        StatusText = "Character selected; waiting for the OK confirmation.";
        log.Information("[CharacterSelectRecovery] Fired _CharaSelectListMenu callbacks 29/0 then 21/0; waiting to click OK.");
        return true;
    }

    private CharacterSelectRecoverySafetySnapshot ReadSafetySnapshot(bool recoveryEnabled)
        => new(
            recoveryEnabled,
            GameHelpers.IsAddonVisible(CharacterSelectAddonName),
            state.AwaitingLoginConfirmation,
            state.LoginConfirmationAccepted);
}
