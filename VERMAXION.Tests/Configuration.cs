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
        public string? SavedConfiguration { get; private set; }

        public void SavePluginConfig(Configuration configuration)
            => SavedConfiguration = Newtonsoft.Json.JsonConvert.SerializeObject(configuration);
    }
}
