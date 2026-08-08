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
        state.UpdateStall(now, recoveryEnabled: false, charaSelectVisible: true);

        var eligibility = CharacterSelectRecoveryPolicy.Evaluate(ReadySnapshot(RecoveryEnabled: false));

        Assert.False(eligibility.CanAttempt);
        Assert.Contains("disabled", eligibility.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DateTime.MinValue, state.ArmedAtUtc);
        Assert.False(state.TryConsumeAutomaticExpiry(now.AddMinutes(10), recoveryEnabled: false));
    }

    [Fact]
    public void CharaSelectVisibilityArmsTheTimerExpiresOnceAndResetsWhenHidden()
    {
        var state = new CharacterSelectStallRecoveryState();
        var startedAt = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

        state.UpdateStall(startedAt, recoveryEnabled: true, charaSelectVisible: true);

        Assert.Equal(startedAt, state.ArmedAtUtc);
        Assert.Equal("Automatic recovery in 5:00", state.GetAutomaticRecoveryStatusText(startedAt));
        Assert.Equal("Automatic recovery in 4:32", state.GetAutomaticRecoveryStatusText(startedAt.AddSeconds(28)));
        Assert.False(state.TryConsumeAutomaticExpiry(
            startedAt + CharacterSelectStallRecoveryState.DefaultRecoveryDelay - TimeSpan.FromTicks(1),
            recoveryEnabled: true));
        Assert.True(state.TryConsumeAutomaticExpiry(
            startedAt + CharacterSelectStallRecoveryState.DefaultRecoveryDelay,
            recoveryEnabled: true));
        Assert.False(state.TryConsumeAutomaticExpiry(
            startedAt + CharacterSelectStallRecoveryState.DefaultRecoveryDelay + TimeSpan.FromMinutes(1),
            recoveryEnabled: true));
        Assert.Equal("Automatic attempt used", state.GetAutomaticRecoveryStatusText(
            startedAt + CharacterSelectStallRecoveryState.DefaultRecoveryDelay));

        state.UpdateStall(startedAt.AddMinutes(7), recoveryEnabled: true, charaSelectVisible: false);
        Assert.Equal(DateTime.MinValue, state.ArmedAtUtc);
        Assert.False(state.AutomaticAttemptIssued);

        var nextVisibleAt = startedAt.AddMinutes(8);
        state.UpdateStall(nextVisibleAt, recoveryEnabled: true, charaSelectVisible: true);
        Assert.Equal(nextVisibleAt, state.ArmedAtUtc);
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
