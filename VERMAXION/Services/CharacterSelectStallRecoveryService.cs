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

    public void QueueManualAttempt()
    {
        if (manualAttemptQueued)
            return;

        manualAttemptQueued = true;
        StatusText = "Manual first-character recovery queued.";
    }

    public void Update(DateTime nowUtc, bool recoveryEnabled, bool stallActive, bool loggedIn)
    {
        state.UpdateStall(nowUtc, recoveryEnabled, stallActive);
        state.ResetAttemptHistoryWhenLoggedOutAndIdle(loggedIn);

        if (state.TryConsumeAutomaticExpiry(nowUtc, recoveryEnabled))
            ExecuteAttempt(recoveryEnabled, "automatic");

        if (manualAttemptQueued)
        {
            manualAttemptQueued = false;
            ExecuteAttempt(recoveryEnabled, "manual");
        }

        if (confirmationClickPending && GameHelpers.ClickYesIfVisible())
            confirmationClickPending = false;

        if (state.AwaitingLoginConfirmation)
            StatusText = confirmationClickPending
                ? "Character selected; waiting for the OK confirmation."
                : "Recovery request sent; waiting for one new login confirmation.";
        else if (state.StallActive && state.ArmedAtUtc != DateTime.MinValue)
        {
            var elapsed = nowUtc.ToUniversalTime() - state.ArmedAtUtc;
            var attempt = state.AutomaticAttemptIssued ? "automatic attempt used" : "automatic attempt armed";
            StatusText = $"Character-select stall {elapsed.TotalMinutes:F1}/{CharacterSelectStallRecoveryState.DefaultRecoveryDelay.TotalMinutes:F1}m; {attempt}.";
        }
        else if (!recoveryEnabled)
        {
            StatusText = "Disabled by global setting.";
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

    private void ExecuteAttempt(bool recoveryEnabled, string source)
    {
        var eligibility = GetEligibility(recoveryEnabled);
        if (!eligibility.CanAttempt)
        {
            LastBlockedReason = eligibility.Reason;
            StatusText = $"{source} recovery blocked: {eligibility.Reason}";
            log.Information($"[CharacterSelectRecovery] {source} attempt blocked: {eligibility.Reason}");
            return;
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
            return;
        }

        if (!state.TryBeginLoginConfirmation(eligibility))
        {
            LastBlockedReason = "A recovery login confirmation is already pending.";
            StatusText = $"{source} recovery blocked: {LastBlockedReason}";
            return;
        }

        LastBlockedReason = string.Empty;
        confirmationClickPending = true;
        StatusText = "Character selected; waiting for the OK confirmation.";
        log.Information("[CharacterSelectRecovery] Fired _CharaSelectListMenu callbacks 29/0 then 21/0; waiting to click OK.");
    }

    private CharacterSelectRecoverySafetySnapshot ReadSafetySnapshot(bool recoveryEnabled)
        => new(
            recoveryEnabled,
            GameHelpers.IsAddonVisible(CharacterSelectAddonName),
            state.AwaitingLoginConfirmation,
            state.LoginConfirmationAccepted);
}
