namespace VERMAXION.Models;

internal enum UiCloseFallbackMode
{
    Always,
    OnlyWhenKnownAddonVisible,
}

internal static class UiCloseFallbackPolicy
{
    public static bool ShouldPressFallbackEscape(UiCloseFallbackMode mode, bool knownAddonWasVisible)
        => mode switch
        {
            UiCloseFallbackMode.Always => true,
            UiCloseFallbackMode.OnlyWhenKnownAddonVisible => knownAddonWasVisible,
            _ => false,
        };
}
