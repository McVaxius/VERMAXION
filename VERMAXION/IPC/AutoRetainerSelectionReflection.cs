using System.Reflection;

namespace VERMAXION.IPC;

// The published character-write IPC does not save. Keep only the existing
// native save binding here; character data now travels through AR's IPC.
internal static class AutoRetainerSelectionReflection
{
    public static bool TryResolveSaveMethod(object autoRetainerPlugin, out MethodInfo saveMethod, out string error)
    {
        saveMethod = null!;
        error = string.Empty;
        var managerType = autoRetainerPlugin.GetType().Assembly.GetType(
            "AutoRetainer.Modules.OfflineDataManager", throwOnError: false, ignoreCase: false);
        if (managerType == null)
        {
            error = "AutoRetainer.Modules.OfflineDataManager was not available.";
            return false;
        }

        var resolvedMethod = managerType.GetMethod(
            "WriteOfflineData",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(bool), typeof(bool)],
            modifiers: null);
        if (resolvedMethod == null)
        {
            error = "OfflineDataManager.WriteOfflineData(bool, bool) was not available.";
            return false;
        }

        saveMethod = resolvedMethod;
        return true;
    }
}
