using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using AethertekUI;

namespace VERMAXION;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the Vermaxion UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文"),
        ("vi","Tiếng Việt"),("pt-BR","Português (Brasil)"),("id","Bahasa Indonesia"),("pl","Polski"),("tr","Türkçe"),("hi","हिन्दी")];
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    private readonly ResourceManager englishManager;
    internal ResourceSet Resources { get; }
    private ResourceSet EnglishResources { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    private readonly (Regex Pattern, string Key, string Prefix, int ArgumentCount)[] messageTemplates;
    private const string MessageParameterPattern = @"\{(\d+)(?::([^}]+))?\}";
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("VERMAXION.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        englishManager=Language=="en" ? manager : new ResourceManager("VERMAXION.Localization.Strings_en",typeof(UiText).Assembly);
        EnglishResources=englishManager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException("en");
        this.pushFont=pushFont;
        // Service messages remain English in logs; only their UI copies are localized.
        var parameter = new Regex(MessageParameterPattern);
        messageTemplates = Language == "en" ? [] : Resources.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(key => parameter.IsMatch(key)).Select(key =>
            {
                var pattern = "^";
                var offset = 0;
                var holes = parameter.Matches(key);
                foreach (Match hole in holes)
                {
                    pattern += Regex.Escape(key[offset..hole.Index]) + $"(?<arg{hole.Groups[1].Value}>.*?)";
                    offset = hole.Index + hole.Length;
                }
                pattern += Regex.Escape(key[offset..]) + "$";
                return (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)), key,
                    key[..holes[0].Index], holes.Cast<Match>().Max(hole => int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture)) + 1);
            }).OrderByDescending(template => parameter.Replace(template.key, "").Length).ToArray();
    }
    internal static string T(string english)
    {
        if (Current.Language == "en") return Current.Resources.GetString(english, false) ?? english;
        if (Current.Resources.GetString(english, true) is { } exact) return exact;
        foreach (var template in Current.messageTemplates)
        {
            if (!english.StartsWith(template.Prefix, StringComparison.Ordinal) || !TemplateLiteralsFit(english, template.Key)) continue;
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;
            // Captured service text is already formatted. Keep names, IDs and leading zeroes
            // intact; typed UI numbers are formatted by F with the selected culture.
            var args = Enumerable.Range(0, template.ArgumentCount)
                .Select(index => (object)match.Groups[$"arg{index}"].Value).ToArray();
            // This template's final argument is our authored visit status, not a native name/ID.
            if (template.Key == "All trainable: trained {0}; skipped {1}; remaining {2}. {3}")
                args[3] = T((string)args[3]);
            return string.Format(Current.Culture, Current.Resources.GetString(template.Key, false)!, args);
        }
        if (english.Contains('\n')) return string.Join("\n", english.Split('\n').Select(T));
        return english; // Names, game data and raw diagnostic values are consumer data.
    }
    private static bool TemplateLiteralsFit(string input, string key)
    {
        var value = input.AsSpan();
        var keyOffset = 0; var valueOffset = 0; var first = true;
        foreach (var hole in Regex.EnumerateMatches(key.AsSpan(), MessageParameterPattern))
        {
            var literal = key.AsSpan(keyOffset, hole.Index - keyOffset);
            if (first)
            {
                if (!value.StartsWith(literal, StringComparison.Ordinal)) return false;
                valueOffset = literal.Length; first = false;
            }
            else
            {
                var found = value[valueOffset..].IndexOf(literal, StringComparison.Ordinal);
                if (found < 0) return false;
                valueOffset += found + literal.Length;
            }
            keyOffset = hole.Index + hole.Length;
        }
        var suffix = key.AsSpan(keyOffset);
        if (value[valueOffset..].EndsWith(suffix, StringComparison.Ordinal)) return true;
        // The original $ anchor also permits one final LF after its last literal.
        return value.Length > valueOffset && value[^1] == '\n' && value[valueOffset..^1].EndsWith(suffix, StringComparison.Ordinal);
    }
    // The caller supplies this character's exact progress summary. Translate its authored
    // numeric fields separately from service/provider detail; runtime status stays unchanged.
    internal static string Verminion(string english, string? progressSummary = null)
    {
        if (Current.Language == "en" || english.Length == 0) return english;
        progressSummary ??= english;
        var summary = T(progressSummary);
        if (english == progressSummary) return summary;
        const string paused = "Paused — use Resume | ";
        if (english == paused + progressSummary) return F(paused + "{0}", summary);
        const string complete = "Complete: ";
        if (english == complete + progressSummary) return T(complete) + summary;
        var suffix = " | " + progressSummary;
        if (!english.EndsWith(suffix, StringComparison.Ordinal)) return T(english);
        var reason = english[..^suffix.Length];
        var completion = reason.StartsWith(complete, StringComparison.Ordinal);
        if (completion) reason = reason[complete.Length..];
        // Preparing Stage is the one service reason that embeds this same summary again.
        // Admit only its authored numeric stage, leaving arbitrary provider detail opaque.
        const string preparing = "Preparing Stage ";
        var stageSuffix = "; " + progressSummary;
        var preparingSummary = reason.StartsWith(preparing, StringComparison.Ordinal) && reason.EndsWith(stageSuffix, StringComparison.Ordinal);
        var stage = preparingSummary ? reason[preparing.Length..^stageSuffix.Length] : string.Empty;
        var detail = stage.Length > 0 && stage.All(char.IsAsciiDigit)
            ? F("Preparing Stage {0}; {1}", stage, summary) : preparingSummary ? reason : T(reason);
        return (completion ? T(complete) : string.Empty) + detail + " | " + summary;
    }
    internal static string Fishing(string english)
        => string.Join(" | ", english.Split(" | ", StringSplitOptions.None).Select(T));

    internal static string Chocobo(string english)
    {
        foreach (var prefix in new[] { "Choke-abo target work: ", "Deferred: " })
            if (english.StartsWith(prefix, StringComparison.Ordinal))
                return F(prefix + "{0}", T(english[prefix.Length..]));
        return T(english);
    }

    internal static string Beastmaster(string english)
    {
        string[] minervaErrors = ["Minerva is owned by another controller.", "Minerva ownership was revoked.",
            "Minerva movement transition is unconfirmed.", "Minerva original state restoration is unconfirmed.",
            "Minerva IPC is unavailable or incompatible.", "Minerva runtime controls are incompatible.",
            "Minerva is not loaded.", "Minerva is loaded, but its runtime state could not be read."];
        const string cleanup = " Cleanup: ";
        var cleanupIndex = english.IndexOf(cleanup, StringComparison.Ordinal);
        if (cleanupIndex > 0)
            return Beastmaster(english[..cleanupIndex]) + F(" Cleanup: {0}", Beastmaster(english[(cleanupIndex + cleanup.Length)..]));
        const string restoration = " Combat restoration pending: ";
        var restorationIndex = english.IndexOf(restoration, StringComparison.Ordinal);
        if (restorationIndex > 0)
            return Beastmaster(english[..restorationIndex]) + " " +
                F("Combat restoration pending: {0}", Beastmaster(english[(restorationIndex + restoration.Length)..]));
        foreach (var prefix in new[] { "Stopped; combat restoration pending: ", "Combat restoration pending: ",
            "RSR runtime state is incompatible: ", "Recovering in five seconds: " })
            if (english.StartsWith(prefix, StringComparison.Ordinal))
            {
                var detail = english[prefix.Length..];
                var minervaComposition = minervaErrors.Any(error => detail.StartsWith(error, StringComparison.Ordinal)) ||
                    detail.Contains(cleanup, StringComparison.Ordinal);
                return F(prefix + "{0}", minervaComposition ? Beastmaster(detail) : T(detail));
            }
        foreach (var first in minervaErrors)
            if (english.StartsWith(first + " ", StringComparison.Ordinal))
                return T(first) + " " + Beastmaster(english[(first.Length + 1)..]);
        const string paused = "Paused: ";
        if (english.StartsWith(paused, StringComparison.Ordinal)) return F("Paused: {0}", Beastmaster(english[paused.Length..]));
        var repair = BeastmasterNumbers(english, "Equipped durability ", "%; repair starts below ", "%.");
        if (repair != null) return F("Equipped durability {0}%; repair starts below {1}%.", repair[0], repair[1]);
        repair = BeastmasterNumbers(english, "Repair complete; equipped durability ", "%.");
        if (repair != null) return F("Repair complete; equipped durability {0}%.", repair[0]);
        repair = BeastmasterNumbers(english, "Repair improved durability to ", "%, but it is still below ", "%; continuing.");
        if (repair != null) return F("Repair improved durability to {0}%, but it is still below {1}%; continuing.", repair[0], repair[1]);
        var entry = BeastmasterNumbers(english, "Requires level ", " and item level ", "; current level ", ", item level ", ".");
        if (entry != null) return F("Requires level {0} and item level {1}; current level {2}, item level {3}.",
            entry[0], entry[1], entry[2], entry[3]);
        return T(english);
    }

    private static string[]? BeastmasterNumbers(string english, params string[] literals)
    {
        if (!english.StartsWith(literals[0], StringComparison.Ordinal)) return null;
        var arguments = new string[literals.Length - 1];
        var offset = literals[0].Length;
        for (var index = 0; index < arguments.Length; index++)
        {
            var start = offset;
            while (offset < english.Length && char.IsDigit(english[offset])) offset++;
            var literal = literals[index + 1];
            if (offset == start || !english.AsSpan(offset).StartsWith(literal, StringComparison.Ordinal)) return null;
            arguments[index] = english[start..offset];
            offset += literal.Length;
        }
        // Preserve the original regex $ allowance for one final LF, outside the captures.
        return offset == english.Length || offset == english.Length - 1 && english[offset] == '\n' ? arguments : null;
    }

    // Collection services retain their original English messages and logs. Resolve their
    // specific compositions here; punctuation-only patterns must never capture names.
    internal static string Collection(string english, IEnumerable<string>? supplyNames = null, string? lastOutcome = null)
    {
        if (Current.Language == "en" || english.Length == 0) return english;
        if (lastOutcome != null)
        {
            const string cleanup = "; cleanup pending: ";
            if (english.StartsWith(lastOutcome + cleanup, StringComparison.Ordinal))
                return Collection(lastOutcome, supplyNames) + T(cleanup) + Collection(english[(lastOutcome.Length + cleanup.Length)..], supplyNames);
            foreach (var suffix in new[] { "; waiting for owned work and rod stow", "; looking for the next opportunity" })
                if (english == lastOutcome + suffix) return Collection(lastOutcome, supplyNames) + T(suffix);
        }
        foreach (var suffix in new[] { "; configured return was rejected", "; configured return retry was rejected", "; configured return failed without verified arrival" })
            if (english.EndsWith(suffix, StringComparison.Ordinal)) return Collection(english[..^suffix.Length], supplyNames) + T(suffix);
        if (english.StartsWith("Fishing ", StringComparison.Ordinal))
            foreach (var suffix in new[] { "; spectral current active", "; waiting for spectral current" })
                if (english.EndsWith(suffix, StringComparison.Ordinal)) return T(english[..^suffix.Length]) + T(suffix);
        if (english.StartsWith("Gathering ", StringComparison.Ordinal))
            foreach (var suffix in new[] { " (expansion recommendation)", " (mandatory)" })
                if (english.EndsWith(suffix, StringComparison.Ordinal)) return T(english[..^suffix.Length]) + T(suffix);
        if (supplyNames != null)
            foreach (var name in supplyNames.OrderByDescending(name => name.Length))
            {
                var mandatory = "Missing mandatory " + name + ": ";
                if (english.StartsWith(mandatory, StringComparison.Ordinal))
                    return F("Missing mandatory {0}: {1}", name, Collection(english[mandatory.Length..], supplyNames));
                var item = name + ": ";
                if (english.StartsWith(item, StringComparison.Ordinal)) return item + Collection(english[item.Length..], supplyNames);
            }
        foreach (var prefix in new[] { "Observation unavailable: ", "Character relog failed: ", "Fisher gearset activation failed: ",
            "Repair rejected: ", "Vendor unavailable: ", "Emptor unavailable: ", "ADS catalog unavailable: ", "Collection native data is unavailable: " })
            if (english.StartsWith(prefix, StringComparison.Ordinal))
                return F(prefix + "{0}", Collection(english[prefix.Length..], supplyNames));
        // The partial-acquisition detail belongs to the provider and stays opaque.
        if (english.StartsWith("Partial acquisition (", StringComparison.Ordinal))
        {
            var match = Regex.Match(english, @"^Partial acquisition \(([^/]+)/([^)]*)\)\. (.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(20));
            if (match.Success) return F("Partial acquisition ({0}/{1}). {2}", match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        }
        // An unrecognized multiline exception/provider response is one opaque value.
        return english.Contains('\n') ? english : T(english);
    }
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),args);
    // Names, account aliases, catalog content and command tokens are data. Callers translate
    // authored status arguments explicitly; interpolation never translates arbitrary data.
    internal static string F(FormattableString text) => string.Format(Current.Culture, T(text.Format), text.GetArguments());
    internal string Format(string key, params object?[] args) => string.Format(Culture, Resources.GetString(key, false) ?? key, args);
    internal string Label(string key) => Resources.GetString(key, false) ?? key;
    internal static IDisposable Font(UiFontRole role) => Current.pushFont(role);
    internal Scope Enter() => new(this);
    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous=current; current=value; }
        public void Dispose() => current=previous;
    }
    internal static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("g",Current.Culture) ?? T("Never");
    internal ushort[] GlyphRanges()
    {
        var chars=RequiredText.SelectMany(t=>MaterialText.NativeGlyphText(t)).Where(c=>!char.IsControl(c))
            .Concat(Enumerable.Range(0x20,0x024F-0x20+1).Select(i=>(char)i))
            .Concat(Enumerable.Range(0x0400,0x052F-0x0400+1).Select(i=>(char)i)).Concat("—").Distinct().Order().ToArray();
        var result=new List<ushort>();
        for(var index=0;index<chars.Length;index++)
        {
            var first=chars[index]; var last=first;
            while(index+1<chars.Length && chars[index+1]==last+1) last=chars[++index];
            result.Add(first); result.Add(last);
        }
        result.Add(0); return result.ToArray();
    }
    internal IEnumerable<string> RequiredText => Values(Resources).Concat(Values(EnglishResources))
        .Concat(Languages.Where(l=>l.Code!="hi").Select(l=>l.Name)).Append("★☆♡⚫—–").Append(Culture.NumberFormat.NumberGroupSeparator);
    internal static IEnumerable<string> Values(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e=>(string)e.Value!);
    public void Dispose()
    {
        manager.ReleaseAllResources();
        if (!ReferenceEquals(englishManager, manager)) englishManager.ReleaseAllResources();
    }
}
