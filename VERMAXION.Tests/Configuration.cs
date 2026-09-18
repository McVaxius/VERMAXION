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
    }

    internal sealed class TestPluginInterface
    {
        private readonly System.Collections.Generic.Dictionary<string, object> sharedData = new();

        public string? SavedConfiguration { get; private set; }

        public void SavePluginConfig(Configuration configuration)
            => SavedConfiguration = Newtonsoft.Json.JsonConvert.SerializeObject(configuration);

        public T GetOrCreateData<T>(string key, System.Func<T> create) where T : class
        {
            if (!sharedData.TryGetValue(key, out var value))
                sharedData[key] = value = create();
            return (T)value;
        }
    }
}
