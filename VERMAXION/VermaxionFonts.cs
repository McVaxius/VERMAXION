using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using AethertekUI;
using AethertekUI.Dalamud;

namespace VERMAXION;

internal sealed class VermaxionFonts : IDisposable
{
    private readonly IFontHandle[] handles;
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal VermaxionFonts(IFontAtlas atlas, ushort[] ranges,string language,string assetRoot)
    {
        handles=VermaxionPresentation.FontSizes.Select((_,index)=>MaterialFontAtlas.CreateFont(atlas,
            VermaxionPresentation.FontWeights[index],VermaxionPresentation.AtlasHeight((UiFontRole)index),ranges,language,assetRoot)).ToArray();
        foreach(var handle in handles) handle.ImFontChanged+=FontChanged;
    }
    private void FontChanged(IFontHandle handle,ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal bool Ready => handles.All(h=>h.Available && h.LoadException is null);
    internal Exception? LoadException => handles.FirstOrDefault(h=>h.LoadException is not null)?.LoadException;
    internal unsafe void CheckGlyphs(IEnumerable<string> strings)
    {
        for(var role = 0; role < handles.Length; role++)
        {
            using var font=handles[role].Lock();
            foreach(var text in strings)
                foreach(var character in MaterialText.NativeGlyphText(text).Where(c=>!char.IsControl(c)))
                    if(ImGui.FindGlyphNoFallback(font.ImFont,character).Handle==null)
                        throw new InvalidOperationException("Required UI glyph missing: U+"+((int)character).ToString("X4")+" in "+(UiFontRole)role+".");
        }
    }
    internal IDisposable Push(UiFontRole role)
    {
        var handle=handles[(int)role];
        if(!handle.Available || handle.LoadException is not null) throw new InvalidOperationException("Vermaxion fonts are not ready.",handle.LoadException);
        return handle.Push();
    }
    public void Dispose() { foreach(var handle in handles) { handle.ImFontChanged-=FontChanged;handle.Dispose(); } }
}
