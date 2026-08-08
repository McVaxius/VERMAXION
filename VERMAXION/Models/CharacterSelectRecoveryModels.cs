using System;

namespace VERMAXION.Models;

internal readonly record struct CharacterSelectRecoverySafetySnapshot(
    bool RecoveryEnabled,
    bool CharaSelectVisible,
    bool AwaitingLoginConfirmation,
    bool LoginConfirmationAccepted);

internal readonly record struct CharacterSelectRecoveryEligibility(bool CanAttempt, string Reason)
{
    public static CharacterSelectRecoveryEligibility Ready() => new(true, "ready");

    public static CharacterSelectRecoveryEligibility Blocked(string reason) => new(false, reason);
}

internal static class CharacterSelectRecoveryPolicy
{
    public const int FirstCharacterIndex = 0;
    public const int SelectFirstCharacterCallback = 29;
    public const int ConfirmFirstCharacterCallback = 21;

    public static CharacterSelectRecoveryEligibility Evaluate(CharacterSelectRecoverySafetySnapshot snapshot)
    {
        if (!snapshot.RecoveryEnabled)
            return CharacterSelectRecoveryEligibility.Blocked("Character-select stall recovery is disabled in global settings.");
        if (snapshot.AwaitingLoginConfirmation)
            return CharacterSelectRecoveryEligibility.Blocked("Waiting for the recovery attempt's login confirmation.");
        if (snapshot.LoginConfirmationAccepted)
            return CharacterSelectRecoveryEligibility.Blocked("The recovery attempt already received its login confirmation.");
        if (!snapshot.CharaSelectVisible)
            return CharacterSelectRecoveryEligibility.Blocked("CharaSelect is not visible.");

        return CharacterSelectRecoveryEligibility.Ready();
    }
}

internal sealed class CharacterSelectStallRecoveryState
{
    public static readonly TimeSpan DefaultRecoveryDelay = TimeSpan.FromMinutes(5);

    public bool StallActive { get; private set; }
    public DateTime ArmedAtUtc { get; private set; } = DateTime.MinValue;
    public bool AutomaticAttemptIssued { get; private set; }
    public bool AwaitingLoginConfirmation { get; private set; }
    public bool LoginConfirmationAccepted { get; private set; }

    public void UpdateStall(DateTime nowUtc, bool recoveryEnabled, bool charaSelectVisible)
    {
        StallActive = charaSelectVisible;
        if (!recoveryEnabled || !charaSelectVisible)
        {
            ArmedAtUtc = DateTime.MinValue;
            AutomaticAttemptIssued = false;
            return;
        }

        if (ArmedAtUtc == DateTime.MinValue)
            ArmedAtUtc = nowUtc.ToUniversalTime();
    }

    public string GetAutomaticRecoveryStatusText(DateTime nowUtc)
    {
        if (AutomaticAttemptIssued)
            return "Automatic attempt used";

        var remaining = DefaultRecoveryDelay - (nowUtc.ToUniversalTime() - ArmedAtUtc);
        var remainingSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"Automatic recovery in {remainingSeconds / 60}:{remainingSeconds % 60:D2}";
    }

    public bool TryConsumeAutomaticExpiry(DateTime nowUtc, bool recoveryEnabled)
    {
        if (!recoveryEnabled ||
            !StallActive ||
            ArmedAtUtc == DateTime.MinValue ||
            AutomaticAttemptIssued ||
            nowUtc.ToUniversalTime() - ArmedAtUtc < DefaultRecoveryDelay)
        {
            return false;
        }

        AutomaticAttemptIssued = true;
        return true;
    }

    public bool TryBeginLoginConfirmation(CharacterSelectRecoveryEligibility eligibility)
    {
        if (!eligibility.CanAttempt || AwaitingLoginConfirmation || LoginConfirmationAccepted)
            return false;

        AwaitingLoginConfirmation = true;
        return true;
    }

    public bool TryAcceptLoginConfirmation()
    {
        if (!AwaitingLoginConfirmation || LoginConfirmationAccepted)
            return false;

        AwaitingLoginConfirmation = false;
        LoginConfirmationAccepted = true;
        return true;
    }

    public void ResetAttemptHistoryWhenLoggedOutAndIdle(bool loggedIn)
    {
        if (loggedIn || StallActive || AwaitingLoginConfirmation)
            return;

        LoginConfirmationAccepted = false;
    }

    public void Reset()
    {
        StallActive = false;
        ArmedAtUtc = DateTime.MinValue;
        AutomaticAttemptIssued = false;
        AwaitingLoginConfirmation = false;
        LoginConfirmationAccepted = false;
    }
}
