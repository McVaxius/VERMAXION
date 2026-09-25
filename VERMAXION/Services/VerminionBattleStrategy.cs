namespace VERMAXION.Services;

/// <summary>Split objective attackers across the field, countering critter armies.</summary>
internal sealed class VerminionBattleStrategy(int stage = 2)
{
    public static bool IsStoneStage(int stage) => stage is 2 or 3 or 5 or 7 or 8 or
        10 or 11 or 13 or 14 or 16 or 17 or 18 or 20 or 21 or 22;
    public const ushort Minion = 587; // Wind-up Wuk Lamat: owned, cost 10, Arcana strength.
    public const string MinionName = "Wind-up Wuk Lamat";
    public const ushort CritterCounter = 82; // Owned Succubus: cost 10, monster, speed 3.
    public const string CritterCounterName = "Wind-up Succubus";
    public enum Action { Wait, SelectGate, Summon, AttackObjective }
    private int lane;
    private int summons;
    public VerminionSummonTracker Reinforcements { get; } = new();
    public bool OpeningComplete => lane >= 3;
    public int Gate => (lane % 3) switch { 0 => 1, 1 => 0, _ => 2 };
    public int EnemyLane => 2 - Gate; // Enemy A/C labels are mirrored across the field.
    public ushort CurrentMinion => stage is 7 or 16 ? CritterCounter : Minion;
    public string CurrentMinionName => stage is 7 or 16 ? CritterCounterName : MinionName;
    public int GroupSize => 6;
    public int MinionCost => 10;
    public uint TargetBaseId => 2006537u;
    public string TargetName => $"Arcana Stone {(char)('A' + EnemyLane)}";

    public Action Decide(bool gateSelected, int unitsAtGate, int usedCapacity, int capacity)
    {
        if (OpeningComplete) return Action.Wait;
        if (!gateSelected) return Action.SelectGate;
        if (unitsAtGate >= GroupSize) return Action.AttackObjective;
        if (summons >= GroupSize || usedCapacity + MinionCost > capacity) return Action.Wait;
        return Action.Summon;
    }

    public void Dispatched(Action action)
    {
        if (action == Action.Summon) ++summons;
        if (action == Action.AttackObjective) { ++lane; summons = 0; }
    }

    public static int ChooseRemainingStone(int?[] hp)
    {
        if (hp.Length != 3 || System.Array.Exists(hp, value => value is null or < 0)) return -1;
        var selected = -1;
        for (var i = 0; i < hp.Length; ++i)
            if (hp[i] > 0 && (selected < 0 || hp[i] < hp[selected])) selected = i;
        return selected;
    }
}

/// <summary>Reserve requests until distinct units appear, independently of movement orders.</summary>
internal sealed class VerminionSummonTracker
{
    private readonly System.Collections.Generic.HashSet<ulong> observedUnits = new();
    private bool baselineObserved;
    public int Pending { get; private set; }
    public bool CanRequest(int usedCapacity, int capacity, int cost = 10) =>
        baselineObserved && Pending < 6 && cost > 0 && usedCapacity >= 0 &&
        usedCapacity + (Pending + 1) * cost <= capacity;
    public void Requested() => ++Pending;

    // Observe independently of camera/selection work. Minions can engage or die
    // before an entire group is simultaneously standing at its summoning gate.
    public int ObserveUnits(System.Collections.Generic.IEnumerable<ulong> units)
    {
        var confirmed = 0;
        foreach (var id in units)
            if (id != 0 && observedUnits.Add(id) && baselineObserved && Pending > 0)
            { --Pending; ++confirmed; }
        baselineObserved = true;
        return confirmed;
    }
}

/// <summary>Boss groups: stay in contact with the current boss/add target and reinforce losses.</summary>
internal sealed class VerminionBossStrategy(int stage = 4)
{
    public const ushort Minion = 130; // Owned Wind-up Alphinaud, cost 10, offensive group special.
    public const string MinionName = "Wind-up Alphinaud";
    public const ushort DefenderMinion = 173;
    public const string DefenderName = "Wind-up Haurchefant";
    public const int DefenderCost = 30;
    public const int DefenderCount = 4;
    // Stage 19: monsters counter Enkidu; critters counter Gilgamesh.
    public ushort CurrentDefenderMinion => stage == 19 ? (ushort)41 : DefenderMinion;
    public string CurrentDefenderName => stage == 19 ? "Goobbue Sproutling" : DefenderName;
    public int CurrentDefenderCost => stage == 19 ? 20 : DefenderCost;
    public int CurrentDefenderCount => stage == 19 ? 8 : DefenderCount;
    // Haurchefant's ATK and poppet affinity penetrate armored monster bosses.
    private bool UsesHaurchefant => stage is 12 or 23 or 24;
    public bool DefendsStone => stage is 12 or 19;
    public ushort CurrentMinion => stage == 15 ? VerminionBattleStrategy.CritterCounter : stage == 19 ? (ushort)243 : UsesHaurchefant ? DefenderMinion : Minion;
    public string CurrentMinionName => stage == 15 ? VerminionBattleStrategy.CritterCounterName : stage == 19 ? "Tora-jiro" : UsesHaurchefant ? DefenderName : MinionName;
    public int MinionCost => stage == 19 ? 20 : UsesHaurchefant ? DefenderCost : 10;
    public int WaveSize => UsesHaurchefant || stage == 19 ? 4 : 6;
    public VerminionSummonTracker Defenders { get; } = new();
    public int AttackCapacity(int capacity, int defenders) => capacity <= 60 ? (stage == 19 ? 0 : capacity) :
        System.Math.Max(0, capacity - System.Math.Max(0, CurrentDefenderCount - defenders) * CurrentDefenderCost);
    public bool CanRequestDefender(int defenders, int usedCapacity, int capacity) =>
        defenders + Defenders.Pending < CurrentDefenderCount &&
        Defenders.CanRequest(usedCapacity + PendingSummons * MinionCost, capacity, CurrentDefenderCost);
    public static bool IsInvulnerabilityAdd(int stage, string name) => stage == 6 &&
        (name.Equals("Infant Imp", System.StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Imp", System.StringComparison.OrdinalIgnoreCase));
    public enum Action { Wait, SelectGate, Summon, SendWave, FollowBoss }
    private readonly VerminionSummonTracker summons = new();
    public int PendingSummons => summons.Pending;
    public bool WaitingForWave => PendingSummons > 0;
    public int ObserveUnits(System.Collections.Generic.IEnumerable<ulong> units) => summons.ObserveUnits(units);

    public Action Decide(bool gateSelected, int readyUnits, int deployedUnits,
        int usedCapacity, int capacity, double waveSeconds, double orderSeconds, bool bossMoved, bool gateAvailable = true, bool assemble = false)
    {
        if (!assemble && (readyUnits >= WaveSize || readyUnits > 0 && waveSeconds >= 60)) return Action.SendWave;
        if (!assemble && deployedUnits >= 4 && orderSeconds >= 10 && bossMoved) return Action.FollowBoss;
        // Reserve capacity for requests whose units have not appeared yet.
        if (gateAvailable && summons.CanRequest(usedCapacity, capacity, MinionCost))
            return gateSelected ? Action.Summon : Action.SelectGate;
        // Repeated selection/movement takes time away from filling the queue.
        // Refresh a stationary group only after reinforcements are accounted for.
        if (!assemble && deployedUnits >= 4 && orderSeconds >= 40) return Action.FollowBoss;
        return Action.Wait;
    }

    public void Dispatched(Action action)
    {
        if (action == Action.Summon) summons.Requested();
    }
}
