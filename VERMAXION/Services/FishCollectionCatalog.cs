using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using VERMAXION.Models;

namespace VERMAXION.Services;

internal sealed class FishCollectionCatalog
{
    public Dictionary<string, string> Sources { get; set; } = new();
    public CollectionFish[] Fish { get; set; } = [];
    public CollectionSpot[] Spots { get; set; } = [];
    public CollectionTerritoryWeather[] WeatherRates { get; set; } = [];
    public uint[] ReusableBait { get; set; } = [];
    public IEnumerable<CollectionFish> Targets => Fish.Where(f => f.Target);
    public static FishCollectionCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VERMAXION.Resources.fish-collection.json")
            ?? throw new InvalidOperationException("Bundled fish catalog is missing.");
        var catalog = JsonSerializer.Deserialize<FishCollectionCatalog>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (catalog.Targets.Count(f => !f.Ocean) != 335 || catalog.Targets.Count(f => f.Ocean) != 13 ||
            catalog.Targets.Where(f => !f.Ocean).Select(f => f.SpotId).Distinct().Count() != 301 ||
            catalog.Targets.Any(f => !f.Ocean && !catalog.Spots.Any(s => s.SpotId == f.SpotId &&
                s.Position is { Length: 3 } && s.Rotation.HasValue && s.TerritoryId != 0)))
            throw new InvalidOperationException("Bundled fish catalog coverage is invalid.");
        return catalog;
    }
}

internal sealed class FishCollectionForecast(FishCollectionCatalog catalog)
{
    private readonly Dictionary<uint, CollectionFish> fishById = catalog.Fish.ToDictionary(f => f.ItemId);
    private readonly Dictionary<uint, uint> territories = catalog.Spots.ToDictionary(s => s.SpotId, s => s.TerritoryId);
    private readonly Dictionary<uint, CollectionWeatherRate[]> rates = catalog.WeatherRates.ToDictionary(r => r.TerritoryId, r => r.Rates);
    private const double SecondsPerHour = 175;
    private const double SecondsPerDay = 4200;
    public static double EorzeaHour(DateTimeOffset now) => (now.ToUnixTimeMilliseconds() / 1000d % SecondsPerDay) / SecondsPerHour;
    public static int WeatherTarget(DateTimeOffset now)
    {
        var seconds = now.ToUnixTimeSeconds();
        var bell = seconds / 175;
        var increment = (bell + 8 - bell % 8) % 24;
        var basis = unchecked((uint)(seconds / 4200 * 100 + increment));
        var step = (basis << 11) ^ basis;
        return (int)(((step >> 8) ^ step) % 100);
    }
    private uint Weather(uint territory, DateTimeOffset time)
    {
        var target = WeatherTarget(time);
        return rates[territory].First(r => target < r.Threshold).WeatherId;
    }
    public IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Windows(CollectionFish fish,
        DateTimeOffset from, DateTimeOffset until)
    {
        var territory = territories[fish.SpotId];
        var firstDay = (long)Math.Floor(from.ToUnixTimeMilliseconds() / 1000d / SecondsPerDay) - 1;
        var lastDay = (long)Math.Ceiling(until.ToUnixTimeMilliseconds() / 1000d / SecondsPerDay);
        (DateTimeOffset Start, DateTimeOffset End)? pending = null;
        for (var day = firstDay; day <= lastDay; day++)
        {
            var startSeconds = day * SecondsPerDay + fish.StartHour * SecondsPerHour;
            var endSeconds = day * SecondsPerDay + fish.EndHour * SecondsPerHour;
            if (fish.EndHour <= fish.StartHour) endSeconds += SecondsPerDay;
            for (var block = (long)Math.Floor(startSeconds / 1400) * 1400; block < endSeconds; block += 1400)
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds((long)(Math.Max(startSeconds, block) * 1000));
                var end = DateTimeOffset.FromUnixTimeMilliseconds((long)(Math.Min(endSeconds, block + 1400) * 1000));
                if (end <= from || start >= until ||
                    (fish.Weather.Length > 0 && !fish.Weather.Contains(Weather(territory, start))) ||
                    (fish.PreviousWeather.Length > 0 && !fish.PreviousWeather.Contains(Weather(territory, start.AddSeconds(-1400))))) continue;
                if (pending is { } prior && prior.End == start) pending = (prior.Start, end);
                else { if (pending.HasValue) yield return pending.Value; pending = (start, end); }
            }
        }
        if (pending.HasValue) yield return pending.Value;
    }
    internal IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> QualifyingWindows(CollectionFish fish,
        DateTimeOffset from, DateTimeOffset until)
    {
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> windows = Windows(fish, from, until);
        foreach (var predator in fish.Predators)
        {
            // Unknown duration grants no guessed carryover. Every predator must share a feasible
            // target interval; separate nonoverlapping predator windows cannot qualify a target.
            var carry = TimeSpan.FromSeconds(fish.IntuitionSeconds ?? 0);
            var available = QualifyingWindows(fishById[predator.ItemId], from - carry, until)
                .Select(w => (w.Start, End: w.End + carry)).ToArray();
            windows = Intersect(windows, available).ToArray();
        }
        return windows;
    }
    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Intersect(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> target,
        (DateTimeOffset Start, DateTimeOffset End)[] predators)
    {
        var cursor = 0;
        (DateTimeOffset Start, DateTimeOffset End)? pending = null;
        foreach (var window in target)
        {
            while (cursor < predators.Length && predators[cursor].End <= window.Start) cursor++;
            for (var i = cursor; i < predators.Length && predators[i].Start < window.End; i++)
            {
                var start = window.Start > predators[i].Start ? window.Start : predators[i].Start;
                var end = window.End < predators[i].End ? window.End : predators[i].End;
                if (end <= start) continue;
                if (pending is { } prior && start <= prior.End)
                    pending = (prior.Start, end > prior.End ? end : prior.End);
                else
                { if (pending.HasValue) yield return pending.Value; pending = (start, end); }
            }
        }
        if (pending.HasValue) yield return pending.Value;
    }

    public FishOpportunity? Next(CollectionFish fish, DateTimeOffset now)
    {
        if (fish.AlwaysAvailable)
        {
            var hour = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 3600 * 3600);
            return new(fish, hour, hour.AddHours(1), TimeSpan.Zero);
        }
        var windows = QualifyingWindows(fish, now, now.AddDays(32)).Take(2).ToArray();
        if (windows.Length == 0) return null;
        return new(fish, windows[0].Start, windows[0].End,
            windows.Length > 1 ? windows[1].Start - windows[0].End : TimeSpan.MaxValue);
    }
}
