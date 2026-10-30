// Only the Dalamud persistence boundary is substituted; tests use the real global Configuration.
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration
    {
        int Version { get; set; }
    }
}

namespace VERMAXION
{
    internal static class Plugin
    {
        public static TestPluginInterface PluginInterface { get; } = new();
        public static TestCommandManager CommandManager { get; } = new();
        public static TestObjectTable ObjectTable { get; } = new();
        public static Dalamud.Plugin.Services.ICondition Condition { get; } = new();
    }

    internal sealed class TestCommandManager
    {
        public System.Func<string, bool>? OnCommand { get; set; }
        public bool ProcessCommand(string command) => OnCommand?.Invoke(command) ?? false;
    }

    internal sealed class TestObjectTable
    {
        public TestPlayer? LocalPlayer { get; set; }
    }

    internal sealed class TestPlayer
    {
        public System.Numerics.Vector3 Position { get; set; }
    }

    internal sealed class TestPluginInterface
    {
        private readonly System.Collections.Generic.Dictionary<string, object> sharedData = new();

        public string? SavedConfiguration { get; private set; }
        public System.Func<string, object?[], object?>? IpcCall { get; set; }
        public System.Func<string, bool>? IpcHasFunction { get; set; }
        public System.Func<string, bool>? IpcHasAction { get; set; }
        public System.Collections.Generic.IReadOnlyList<TestInstalledPlugin> InstalledPlugins { get; set; } = [];

        public Dalamud.Plugin.Ipc.ICallGateSubscriber<T> GetIpcSubscriber<T>(string name)
            => new(args => IpcCall!(name, args), () => IpcHasFunction?.Invoke(name) ?? true,
                () => IpcHasAction?.Invoke(name) ?? true);
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<T1, T> GetIpcSubscriber<T1, T>(string name)
            => new(args => IpcCall!(name, args), () => IpcHasFunction?.Invoke(name) ?? true,
                () => IpcHasAction?.Invoke(name) ?? true);
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<T1, T2, T3, T> GetIpcSubscriber<T1, T2, T3, T>(string name)
            => new(args => IpcCall!(name, args), () => IpcHasFunction?.Invoke(name) ?? true);
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<T1, T2, T> GetIpcSubscriber<T1, T2, T>(string name)
            => new(args => IpcCall!(name, args), () => IpcHasFunction?.Invoke(name) ?? true);
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<T1, T2, T3, T4, T> GetIpcSubscriber<T1, T2, T3, T4, T>(string name)
            => new(args => IpcCall!(name, args), () => IpcHasFunction?.Invoke(name) ?? true);
        public Dalamud.Plugin.Ipc.ICallGateSubscriber<T1, T2, T3, T4, T5, T> GetIpcSubscriber<T1, T2, T3, T4, T5, T>(string name)
            => new(args => IpcCall!(name, args), () => IpcHasFunction?.Invoke(name) ?? true);

        public void SavePluginConfig(Configuration configuration)
            => SavedConfiguration = Newtonsoft.Json.JsonConvert.SerializeObject(configuration);

        public T GetOrCreateData<T>(string key, System.Func<T> create) where T : class
        {
            if (!sharedData.TryGetValue(key, out var value))
                sharedData[key] = value = create();
            return (T)value;
        }
    }

    internal sealed record TestInstalledPlugin(string InternalName, bool IsLoaded);
}
