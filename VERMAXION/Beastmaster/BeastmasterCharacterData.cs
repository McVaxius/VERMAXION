using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Beastmaster;

public partial record CrucibleFamiliar
{
    public uint Number { get; set; }
    public CrucibleFamiliar() { }
    public CrucibleFamiliar(uint number) => Number = number;
    public int Rank { get; set { if (value > field) field = value; } }
    public int Hp { get; set; }
    public int Strength { get; set; }
    public int PhysicalResistance { get; set; }
    public int Constitution { get; set; }
    public int Intelligence { get; set; }
    public int MagicResistance { get; set; }
    public string Exp { get; set; } = string.Empty;
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class BeastmasterCharacterData
{
    public SortedDictionary<uint, CrucibleFamiliar> Familiars { get; set; } = [];
    public List<uint> Team { get; set; } = [];
    public HashSet<uint> Owned { get; set; } = [];
    public DateTime OwnershipObservedAtUtc { get; set; }
    public DateTime RanksObservedAtUtc { get; set; }
    public BeastmasterSettings? ManualSettings { get; set; }
    public BeastmasterRunSetup? ScheduledSetup { get; set; }

    [Newtonsoft.Json.JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
    public bool HasRecordedBeasts => Owned.Count > 0 || Team.Count > 0 ||
        Familiars.Values.Any(f => f.Rank > 0 || f.Hp > 0 || f.Strength > 0 || f.Constitution > 0 ||
            f.Intelligence > 0 || f.PhysicalResistance > 0 || f.MagicResistance > 0 ||
            !string.IsNullOrWhiteSpace(f.Exp) || f.ObservedAtUtc != DateTime.MinValue);

    public BeastmasterSettings GetSettings(BeastmasterSettings legacy)
    {
        ManualSettings ??= legacy.Clone();
        ScheduledSetup ??= BeastmasterRunSetup.From(ManualSettings);
        return ManualSettings;
    }
    public void EnsureFamiliars(IEnumerable<uint> catalog)
    {
        foreach (var number in catalog)
            if (!Familiars.ContainsKey(number)) Familiars[number] = new CrucibleFamiliar(number);
    }
    public BeastmasterCharacterData Clone() => new()
    {
        Familiars = new SortedDictionary<uint, CrucibleFamiliar>(Familiars.ToDictionary(e => e.Key, e => e.Value with { })),
        Team = [..Team],
        Owned = [..Owned], OwnershipObservedAtUtc = OwnershipObservedAtUtc,
        RanksObservedAtUtc = RanksObservedAtUtc, ManualSettings = ManualSettings?.Clone(),
        ScheduledSetup = ScheduledSetup?.Clone(),
    };
}
