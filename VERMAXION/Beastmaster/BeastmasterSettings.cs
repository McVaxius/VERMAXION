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
        RepairMode = RepairMode, RepairThreshold = RepairThreshold,
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
