using System;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class CharacterSelectRecoveryPolicyTests
{
    [Fact]
    public void GlobalSettingOffBlocksManualAndAutomaticRecovery()
    {
        var state = new CharacterSelectStallRecoveryState();
        var now = DateTime.UtcNow;
        state.UpdateStall(now, recoveryEnabled: false, stallActive: true);

        var eligibility = CharacterSelectRecoveryPolicy.Evaluate(ReadySnapshot(RecoveryEnabled: false));

        Assert.False(eligibility.CanAttempt);
        Assert.Contains("disabled", eligibility.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DateTime.MinValue, state.ArmedAtUtc);
        Assert.False(state.TryConsumeAutomaticExpiry(now.AddMinutes(10), recoveryEnabled: false));
    }

    [Fact]
    public void StallTimerArmsExpiresOnceAndResetsWhenTheStallEnds()
    {
        var state = new CharacterSelectStallRecoveryState();
        var startedAt = DateTime.UtcNow;

        state.UpdateStall(startedAt, recoveryEnabled: true, stallActive: true);

        Assert.Equal(startedAt, state.ArmedAtUtc);
        Assert.False(state.TryConsumeAutomaticExpiry(
            startedAt + CharacterSelectStallRecoveryState.DefaultRecoveryDelay - TimeSpan.FromTicks(1),
            recoveryEnabled: true));
        Assert.True(state.TryConsumeAutomaticExpiry(
            startedAt + CharacterSelectStallRecoveryState.DefaultRecoveryDelay,
            recoveryEnabled: true));
        Assert.False(state.TryConsumeAutomaticExpiry(
            startedAt + CharacterSelectStallRecoveryState.DefaultRecoveryDelay + TimeSpan.FromMinutes(1),
            recoveryEnabled: true));

        state.UpdateStall(startedAt.AddMinutes(7), recoveryEnabled: true, stallActive: false);
        Assert.Equal(DateTime.MinValue, state.ArmedAtUtc);
        Assert.False(state.AutomaticAttemptIssued);

        var nextStall = startedAt.AddMinutes(8);
        state.UpdateStall(nextStall, recoveryEnabled: true, stallActive: true);
        Assert.Equal(nextStall, state.ArmedAtUtc);
        Assert.False(state.AutomaticAttemptIssued);
    }

    [Fact]
    public void ManualButtonEligibilityRetainsEverySafetyGate()
    {
        var noCharacterSelect = ReadySnapshot(CharaSelectVisible: false);

        Assert.False(CharacterSelectRecoveryPolicy.Evaluate(noCharacterSelect).CanAttempt);
        Assert.True(CharacterSelectRecoveryPolicy.Evaluate(ReadySnapshot()).CanAttempt);
    }

    [Fact]
    public void RecoveryUsesTheFirstCharacterCallbackSequence()
    {
        Assert.Equal(0, CharacterSelectRecoveryPolicy.FirstCharacterIndex);
        Assert.Equal(29, CharacterSelectRecoveryPolicy.SelectFirstCharacterCallback);
        Assert.Equal(21, CharacterSelectRecoveryPolicy.ConfirmFirstCharacterCallback);
        Assert.True(CharacterSelectRecoveryPolicy.Evaluate(ReadySnapshot()).CanAttempt);
    }

    [Fact]
    public void RecoveryAcceptsOnlyOneNewLoginConfirmation()
    {
        var state = new CharacterSelectStallRecoveryState();
        var eligibility = CharacterSelectRecoveryPolicy.Evaluate(ReadySnapshot());

        Assert.True(state.TryBeginLoginConfirmation(eligibility));
        Assert.True(state.AwaitingLoginConfirmation);
        Assert.True(state.TryAcceptLoginConfirmation());
        Assert.True(state.LoginConfirmationAccepted);
        Assert.False(state.TryAcceptLoginConfirmation());
        Assert.False(state.TryBeginLoginConfirmation(eligibility));
    }

    private static CharacterSelectRecoverySafetySnapshot ReadySnapshot(
        bool RecoveryEnabled = true,
        bool CharaSelectVisible = true)
        => new(
            RecoveryEnabled,
            CharaSelectVisible,
            AwaitingLoginConfirmation: false,
            LoginConfirmationAccepted: false);
}
