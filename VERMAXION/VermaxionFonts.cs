using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using AethertekUI;
using AethertekUI.Dalamud;

namespace VERMAXION;

internal sealed class VermaxionFonts : IDisposable
{
    private readonly IFontAtlas atlas;
    private readonly Dictionary<UiFontRole, IFontHandle> roles = [];
    private readonly Dictionary<(MaterialFontWeight Weight, float Height), IFontHandle> faces = [];
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal VermaxionFonts(IFontAtlas atlas, ushort[] ranges, string language, string assetRoot)
    {
        this.atlas = atlas;
        var glyphCount = 0;
        for (var index = 0; index + 1 < ranges.Length; index += 2)
            glyphCount += ranges[index + 1] - ranges[index] + 1;
        var pageSize = glyphCount > 1000 ? 2048 : 1024;
        atlas.BuildStepChange += MaterialFontAtlas.ReleaseRasterizedInputs;
        try
        {
            // Windows can open sibling views or switch density during the same draw pass.
            foreach (var role in Enum.GetValues<UiFontRole>().Where(role => role != UiFontRole.Counter))
            {
                var weight = VermaxionPresentation.FontWeights[(int)role];
                var height = VermaxionPresentation.AtlasHeight(role);
                if (!faces.TryGetValue((weight, height), out var handle))
                {
                    handle = MaterialFontAtlas.CreateFont(atlas, weight, height, ranges, language, assetRoot,
                        pageSize, includeHostLanguage: false);
                    faces.Add((weight, height), handle);
                    handle.ImFontChanged += FontChanged;
                }
                roles.Add(role, handle);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IEnumerable<float> Sizes => roles.Keys.Select(role => VermaxionPresentation.FontSizes[(int)role]).Distinct();
    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => faces.Values.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => faces.Values.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        foreach (var (role, handle) in roles)
        {
            using var font = handle.Lock();
            foreach(var text in strings)
                foreach(var character in MaterialText.NativeGlyphText(text).Where(c=>!char.IsControl(c)))
                    if(ImGui.FindGlyphNoFallback(font.ImFont,character).Handle==null)
                        throw new InvalidOperationException("Required UI glyph missing: U+"+((int)character).ToString("X4")+" in "+role+".");
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle = roles[role];
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("Vermaxion fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose()
    {
        atlas.BuildStepChange -= MaterialFontAtlas.ReleaseRasterizedInputs;
        foreach (var handle in faces.Values)
        {
            handle.ImFontChanged -= FontChanged;
            handle.Dispose();
        }
        atlas.Dispose();
    }
}
