namespace VERMAXION.Models;

internal readonly record struct AutoRetainerSelectionReadResult(
    bool Success,
    bool Enabled,
    string Error)
{
    public static AutoRetainerSelectionReadResult Known(bool enabled)
        => new(true, enabled, string.Empty);

    public static AutoRetainerSelectionReadResult Failed(string error)
        => new(false, false, error);
}

internal readonly record struct AutoRetainerSelectionWriteResult(
    bool Success,
    bool Enabled,
    bool SaveInvoked,
    string Error)
{
    public static AutoRetainerSelectionWriteResult Verified(bool enabled)
        => new(true, enabled, true, string.Empty);

    public static AutoRetainerSelectionWriteResult Failed(
        string error,
        bool enabled = false,
        bool saveInvoked = false)
        => new(false, enabled, saveInvoked, error);
}

internal interface IAutoRetainerSelectionAccessor
{
    AutoRetainerSelectionReadResult ReadCurrentCharacterSelection(ulong localContentId);

    AutoRetainerSelectionWriteResult WriteCurrentCharacterSelection(
        ulong localContentId,
        bool enabled);
}

internal enum AutoRetainerSelectionGuardState
{
    Inactive,
    AwaitingWorkStart,
    Observing,
    Repairing,
    Completed,
}
