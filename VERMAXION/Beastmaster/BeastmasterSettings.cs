using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VERMAXION.Beastmaster;

public enum CrucibleTeamMode { Recommended, Leveling, Custom }
public enum CrucibleLevelingMode { Full, Minus_3, Only_3, Carry_Minus_2 }
public enum CrucibleDegree { Standard, First, Second, Third }
public enum BeastmasterRunGoal { FixedRuns, TrainOwned }
public enum BeastmasterTaskFrequency { EveryAR, Daily, Weekly, Monthly }
public enum BeastmasterRepairMode { Disabled, Self, NpcNoInn, NpcNearby, NpcInn }
public enum BeastmasterCombatProvider { BossModReborn, Minerva }

public sealed class BeastmasterSettings
{
    public BeastmasterRunGoal Goal { get; set; }
    public uint Board { get; set; } = 1;
    // Zero retains the board default for existing settings and scheduled runs.
    public int TrainingTargetRank { get; set => field = Math.Clamp(value, 0, 25); }
    public int RunCount { get; set; } = 1;
    public HashSet<uint>? WeeklyBoards { get; set; }
    public string CompletionCommand { get; set; } = string.Empty;
    public BeastmasterRepairMode RepairMode { get; set => field = Enum.IsDefined(value) ? value : BeastmasterRepairMode.Self; } = BeastmasterRepairMode.Self;
    public int RepairThreshold { get; set => field = Math.Clamp(value, 0, 100); } = 75;
    public BeastmasterCombatProvider CombatProvider
    {
        get;
        set => field = Enum.IsDefined(value) ? value : BeastmasterCombatProvider.BossModReborn;
    }
    public CrucibleDegree Degree { get; set; } = CrucibleDegree.Standard;
    public CrucibleTeamMode TeamMode { get; set; } = CrucibleTeamMode.Recommended;
    public CrucibleLevelingMode LevelingMode { get; set; } = CrucibleLevelingMode.Minus_3;
    public List<uint> CustomTeam { get; set; } = [];
    public List<BeastmasterShopList> ShopLists { get; set; } = [];
    public int ShopListIndex { get; set; }
    public List<uint> ShopGearOrder { get; set; } = [];
    public List<BeastTierList> BeastTierLists { get; set; } = [];
    public int BeastTierListIndex { get; set; }
    public bool FightPicks { get; set; } = true;
    public bool Loot { get; set; } = true;
    public bool Treasure { get; set; } = true;
    public bool Shop { get; set; } = true;
    public bool Rest { get; set; } = true;
    public bool Items { get; set; } = true;
    public bool RespectGearRequirements { get; set; } = true;
    public int RecommendedTeamCap { get; set => field = Math.Clamp(value, 3, 15); } = 15;
    public int ClassificationLock { get; set; }

    public BeastmasterSettings Clone() => new()
    {
        Goal = Goal, Board = Board, TrainingTargetRank = TrainingTargetRank, RunCount = RunCount,
        WeeklyBoards = WeeklyBoards == null ? null : [..WeeklyBoards], CompletionCommand = CompletionCommand,
        RepairMode = RepairMode, RepairThreshold = RepairThreshold, CombatProvider = CombatProvider,
        Degree = Degree, TeamMode = TeamMode, LevelingMode = LevelingMode,
        CustomTeam = [..CustomTeam], ShopLists = ShopLists.Select(list => list.Clone()).ToList(),
        ShopListIndex = ShopListIndex, ShopGearOrder = [..ShopGearOrder],
        BeastTierLists = BeastTierLists.Select(list => list.Clone()).ToList(),
        BeastTierListIndex = BeastTierListIndex, FightPicks = FightPicks, Loot = Loot,
        Treasure = Treasure, Shop = Shop, Rest = Rest, Items = Items,
        RespectGearRequirements = RespectGearRequirements, RecommendedTeamCap = RecommendedTeamCap,
        ClassificationLock = ClassificationLock,
    };
}

public interface IBeastmasterList { string Name { get; set; } }

public partial class BeastmasterShopList : IBeastmasterList
{
    public string Name { get; set; } = "Default";
    public List<Entry> Order { get; set; } = [];
    public BeastmasterShopList Clone(string? name = null) => new()
    {
        Name = name ?? Name,
        Order = Order.Select(e => new Entry { ID = e.ID, Blocked = e.Blocked }).ToList(),
    };

    public partial class Entry
    {
        public uint ID { get; set; }
        public bool Blocked { get; set; }
        public static implicit operator Entry(uint id) => new() { ID = id };
        public static implicit operator Entry(long id) => (uint)id;
    }
}

[JsonObject(MemberSerialization.OptOut)]
public sealed class BeastTierList : List<BeastTierList.Entry>, IBeastmasterList
{
    public sealed class Entry
    {
        public uint ID { get; set; }
        public bool PickBlock { get; set; }
        public bool BattleBlock { get; set; }
        public static implicit operator Entry(uint id) => new() { ID = id };
        public static implicit operator Entry(long id) => (uint)id;
    }

    public static readonly uint[] beastTierListDefault = new uint[]
    {
        13,20,27,19,7,44,2,17,15,39,10,47,37,6,35,8,11,18,50,36,46,4,16,23,41,
        34,21,29,38,3,28,48,33,31,43,5,14,12,45,26,49,1,40,9,42,22,25,32,24,30,
    };

    private static readonly (string Name, uint[] Prefix, bool ExcludeLateBeasts)[] premadeDefinitions =
    [
        ("Board 1 - First clear - Standard", [16,8,10,13,2,27,19,7], false),
        ("Board 2 - First clear - Standard", [11,19,32,38,23,13,20,5], false),
        ("Board 3 - First clear - Standard - Progression roster", [1,26,33,38,23,13,20,32], true),
        ("Board 3 - First clear - Standard - Expanded roster", [1,50,33,47,38,23,13,44], false),
        ("Board 4 - First clear - Standard", [1,50,33,23,9,47,13,20], false),
        ("Board 5 - First clear - Standard", [1,50,33,23,20,9,47,13], false),
        ("Board 1 - Farming - All difficulties", [11,19,32,50,47,1,5,9], false),
        ("Board 2 - Farming - All difficulties", [11,19,32,20,23,5,9,50], false),
        ("Board 3 - Farming - All difficulties", [11,19,32,50,47,1,33,38], false),
        ("Board 4 - Farming - All difficulties", [11,19,32,23,9,50,47,5], false),
        ("Board 5 - Farming - All difficulties", [11,19,32,23,50,20,9,47], false),
    ];

    public static IEnumerable<string> PremadeNames => premadeDefinitions.Select(definition => definition.Name);

    public static BeastTierList CreatePremade(string name)
    {
        var definition = premadeDefinitions.FirstOrDefault(definition => definition.Name == name);
        if (definition.Name is null) throw new ArgumentException("Unknown premade beast priority list.", nameof(name));
        var list = new BeastTierList { Name = definition.Name };
        list.AddRange(definition.Prefix.Select(id => new Entry { ID = id }));
        list.Complete();
        if (definition.ExcludeLateBeasts)
            foreach (var entry in list) entry.PickBlock = entry.ID is >= 43 and <= 50;
        return list;
    }

    public string Name { get; set; } = "Default";
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<Entry> Entries { get => this.ToList(); set { Clear(); AddRange(value); } }
    [JsonIgnore] public bool IsCompleted => beastTierListDefault.All(id => this.Any(e => e.ID == id));
    [JsonIgnore] public bool OrderChanged => !this.Select(e => e.ID).SequenceEqual(beastTierListDefault);
    public void Complete(IEnumerable<uint>? catalog = null)
    {
        foreach (var id in beastTierListDefault.Concat(catalog ?? []).Distinct())
            if (this.All(e => e.ID != id)) Add(new Entry { ID = id });
    }
    public static BeastTierList Default() { var list = new BeastTierList(); list.Complete(); return list; }
    public BeastTierList Clone(string? name = null)
    {
        var clone = new BeastTierList { Name = name ?? Name };
        clone.AddRange(this.Select(e => new Entry { ID = e.ID, PickBlock = e.PickBlock, BattleBlock = e.BattleBlock }));
        return clone;
    }
}
