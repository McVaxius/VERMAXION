using System;

namespace VERMAXION.Models;

public enum RefillListingPacingEvent
{
    MenuOrClick,
    WithdrawalVerified,
    WithdrawalNotVerified,
}

public sealed record RefillListingPacingSnapshot(int ActionDelayMs, int InterItemDelayMs)
{
    public const int DefaultDelayMs = 250;
    public const int MinimumDelayMs = 0;
    public const int MaximumDelayMs = 2000;
    public const int VerificationPollDelayMs = 500;

    public static RefillListingPacingSnapshot Capture(int actionDelayMs, int interItemDelayMs)
        => new(Clamp(actionDelayMs), Clamp(interItemDelayMs));

    public int SelectDelay(RefillListingPacingEvent pacingEvent)
        => pacingEvent switch
        {
            RefillListingPacingEvent.MenuOrClick => ActionDelayMs,
            RefillListingPacingEvent.WithdrawalVerified => InterItemDelayMs,
            RefillListingPacingEvent.WithdrawalNotVerified => VerificationPollDelayMs,
            _ => throw new ArgumentOutOfRangeException(nameof(pacingEvent), pacingEvent, null),
        };

    private static int Clamp(int value) => Math.Clamp(value, MinimumDelayMs, MaximumDelayMs);
}
