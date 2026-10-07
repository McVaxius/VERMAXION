using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using AethertekUI;

namespace VERMAXION;

internal sealed class VermaxionFonts : IDisposable
{
    private readonly IFontHandle[] handles;
    private int generation;
    internal int Generation => System.Threading.Volatile.Read(ref generation);
    internal VermaxionFonts(IFontAtlas atlas, ushort[] ranges,string language)
    {
        handles=VermaxionPresentation.FontSizes.Select((size,index)=>atlas.NewDelegateFontHandle(toolkit=>toolkit.OnPreBuild(build=>
        {
            build.NewImAtlas.TexDesiredWidth=4096;
            build.NewImAtlas.TexDesiredHeight=4096;
            size=VermaxionPresentation.AtlasHeight((UiFontRole)index);
            var config=new SafeFontConfig { SizePx=size, GlyphRanges=ranges };
            build.Font=build.AddFontFromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),VermaxionPresentation.FontFiles[index]),config);
            // Segoe UI text faces omit symbols used by the DTR tooltip, including U+26AB.
            // Borrow Windows' symbol face into the managed atlas without distributing the font.
            build.AddFontFromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisym.ttf"),
                new SafeFontConfig { SizePx=size, MergeFont=build.Font, GlyphRanges=ranges });
            // Verified bundled TTC faces: JP=0, KR=1, SC=2, TC=3.
            build.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular,new SafeFontConfig
            {
                SizePx=size, MergeFont=build.Font, GlyphRanges=ranges,
                FontNo=language switch { "ko"=>1, "zh-Hans" or "zh-CN"=>2, "zh-Hant" or "zh-TW"=>3, _=>0 },
            });
            build.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx=size, MergeFont=build.Font });
            build.AddGameSymbol(new SafeFontConfig { SizePx=size,MergeFont=build.Font });
        }))).ToArray();
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
