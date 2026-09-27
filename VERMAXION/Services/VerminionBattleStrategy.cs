using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Services;

internal sealed record VerminionMinion(ushort Id, string Name, int Cost, string Acquisition);
internal sealed record VerminionVendorMinion(ushort MinionId, uint ItemId, string Name, uint Gil, uint Mgp = 0)
{
    public string Shop => Mgp > 0 ? "ShopExchangeCurrency" : "Shop";
    public uint Price => Mgp > 0 ? Mgp : Gil;
    public string Currency => Mgp > 0 ? "MGP" : "gil";
}

/// <summary>Accessible baseline choices. Guide adaptations still require live verification.</summary>
internal static class VerminionRoster
{
    internal static readonly ushort[] GentlemanQuests =
    [1204, 1205, 1206, 1207, 1315, 1316, 1317, 1318, 1438, 1439, 1440, 1441, 166, 202, 203, 204, 490, 491, 492, 493, 502];
    public static ushort[] AcquisitionQuests(ushort minion) => minion == 21 ? GentlemanQuests : [];
    public static readonly VerminionVendorMinion MammetOffer = new(2, 6004, "Mammet #001", 2400);
    public static readonly VerminionVendorMinion HatchlingOffer = new(3, 6005, "Wayward Hatchling", 2400);
    public static readonly VerminionVendorMinion NeroOffer = new(174, 14096, "Wind-up Nero tol Scaeva", 0, 30000);
    public static readonly VerminionVendorMinion ZuOffer = new(83, 7565, "Zu Hatchling", 0, 10000);
    private static readonly VerminionVendorMinion[] EntryOffers =
    [
        MammetOffer,
        HatchlingOffer,
        new(1, 6003, "Cherry Bomb", 2400),
    ];

    public static VerminionVendorMinion? VendorOffer(uint itemId, ushort minionId) =>
        EntryOffers.Append(NeroOffer).Append(ZuOffer).FirstOrDefault(offer => offer.ItemId == itemId && offer.MinionId == minionId);

    // Prefer already registered minions, then buy only the distinct minions
    // needed for entry. Mammet also supplies the ordinary-stage battle roster.
    public static VerminionVendorMinion[] EntryPurchases(IEnumerable<ushort> owned)
    {
        var registered = owned.Where(id => id != 0).ToHashSet();
        return EntryOffers.Where(offer => !registered.Contains(offer.MinionId))
            .Take(System.Math.Max(0, 3 - registered.Count)).ToArray();
    }

    public static readonly VerminionMinion Mammet = new(2, "Mammet #001", 10,
        "2,400 gil from the Minion Trader in Minion Square or city minion vendors; register the item before battle.");
    public static readonly VerminionMinion Hatchling = new(3, "Wayward Hatchling", 15,
        "2,400 gil from the Minion Trader in Minion Square or city minion vendors; register the item before battle.");
    public static readonly VerminionMinion Airship = new(52, "Wind-up Airship", 25,
        "Reward from your starting city's level 15 Envoy main scenario quest; register the item.");
    public static readonly VerminionMinion Bat = new(26, "Baby Bat", 10,
        "2,400 gil from Junkmonger Nonoroon at Poor Maid's Mill, Upper La Noscea, after the FATE Poor Maid's Misfortune. This vendor is conditional; automatic FATE completion is not supported.");
    public static readonly VerminionMinion Nero = new(174, "Wind-up Nero tol Scaeva", 20,
        "30,000 MGP from the Minion Trader in Minion Square; permanent vendor stock with no additional unlock.");
    public static readonly VerminionMinion Zu = new(83, "Zu Hatchling", 10,
        "10,000 MGP from the Minion Trader in Minion Square; permanent vendor stock with no additional unlock.");
    public static readonly VerminionMinion Gentleman = new(21, "Wind-up Gentleman", 30,
        "Reward from the level 50 ARR Hildibrand quest Her Last Vow. Acquire through the Questionable handoff below, then Resume to register the reward before Stages 23 and 24. The minion cannot be bought for gil or MGP.");

    public static VerminionMinion Attacker(int stage) => stage is 7 or 15 or 16 ? Bat :
        stage is 23 or 24 ? Gentleman : VerminionBattleStrategy.IsStoneStage(stage) ? Mammet : Airship;
    public static bool HasDefenders(int stage) => stage is 6 or 20;
    public static VerminionMinion[] Required(int stage) => stage is < 2 or > 24 ? [] :
        HasDefenders(stage) ? [Attacker(stage), stage == 20 ? Hatchling : Bat] : [Attacker(stage)];

    // Participation needs a fighting roster only while unlocking Stage 2.
    public static bool NeedsBattleRoster(int stage, bool campaign, Models.VerminionMode mode, uint clears) =>
        stage > 1 && (campaign || mode == Models.VerminionMode.WinTarget || (clears & 2) == 0);

    public static string? Missing(int stage, System.Func<ushort, bool?> owns)
    {
        foreach (var minion in Required(stage))
        {
            var owned = owns(minion.Id);
            if (owned == null) return "Minion ownership is unavailable; log in and wait for character data.";
            if (owned == false) return $"Stage {stage} requires {minion.Name}. {minion.Acquisition} No purchase made.";
        }
        return null;
    }

    public static string Tactics(int stage) => stage switch
    {
        1 => "Follow the tutorial prompts. Three registered minions are required to enter; the tutorial supplies its combat minions.",
        2 or 3 or 5 or 8 or 10 or 13 or 14 or 17 or 18 or 21 =>
            "Mammet groups attack the center, then side stones. Replace losses; the guide explicitly allows Mammets as stone attackers.",
        20 => "Six Mammets per lane attack stones. Four Wayward Hatchlings counter poppets at the center stone, then defend a surviving stone. The guide suggests defensive support; this vendor adaptation is under live testing. Reserve 60 capacity for the defenders and use Choco Shuffle near enemies.",
        7 or 16 => "Baby Bats counter critters and use area damage over time. Split across stones and reinforce. This substitutes a common vendor minion for the previous collection-specific attacker.",
        11 => "Mammet groups split across stones. The guide recommends extra combat support here; the vendor-only composition still needs a fresh clear.",
        22 => "Split Mammet groups across stones, redirect survivors and replace losses. The guide suggests Wind-up Odin support; the Mammet-only baseline has one observed clear.",
        4 => "Airships follow the boss between stones; group four to use Cargo's area ATK buff. Replace casualties and avoid wasting orders while attacking.",
        6 => "Airships switch to the Imps that maintain invulnerability, then return to the boss. Four Baby Bats defend the center stone.",
        9 => "Fast Airships carry bombs from the lever, place and arm them by the invulnerable slime, then attack the vulnerable final phase.",
        12 => "Airships defend one stone and approach the boss from behind. Use surviving gates for replacements when gates are destroyed.",
        15 => "Assemble 24 Baby Bats before engaging Odin; recover before the final phase and use their damage-over-time special during the final burn. This vendor substitution is unverified.",
        19 => "Up to nine Airships defend the center stone and intercept Enkidu. Do not chase Gilgamesh away; retreat to heal during his ATK/DEF/SPD buff and return afterward. Use Cargo's area ATK buff on the group.",
        23 => "Wind-up Gentlemen rush Twintania as they spawn. Keep replacements queued and ignore resistant nodes. Save Power of Deduction for the final 20% while eight attackers survive: it buffs nearby allies but withdraws its four-minion action party. This published composition still needs a live clear by this bot; the tested Airship, Nero and Zu substitutions failed.",
        24 => "The guide uses Wind-up Gentlemen: send replacements to Bahamut as they spawn, move the army out of plain red circles, and send one healthy minion into each red pillar circle. Focus one Clockwork Twintania when both adds appear, then return to Bahamut. Preserve units instead of using their party-withdrawing special. Add targeting is implemented but unverified; tower assignment and circle avoidance are still missing, so ordinary admission remains blocked.",
        _ => "No challenge selected.",
    };

    public static string GuideUrl(int stage) => "https://ffxiverminion.com/" + (stage switch
    {
        2 => "stage2-hatching-a-plan", 3 => "stage3-the-first-move", 4 => "stage4-little-big-beast",
        5 => "stage5-turning-tribes", 6 => "stage6-off-the-deepcroft", 7 => "stage7-rivals",
        8 => "stage8-always-darkest", 9 => "stage9-mine-your-minions", 10 => "stage10-children-of-mandragora",
        11 => "stage11-the-queen-and-i", 12 => "stage12-breakout", 13 => "stage13-my-name-is-cid",
        14 => "stage14-like-a-nut", 15 => "stage15-urth-s-spout", 16 => "stage16-exodus",
        17 => "stage17-over-the-wall", 18 => "stage18-the-hunt", 19 => "stage19-battle-on-the-bitty-bridge",
        20 => "stage20-guiding-light", 21 => "stage21-wise-words", 22 => "stage22-world-of-poor-lighting",
        23 => "stage23-the-binding-coil", 24 => "stage24-the-final-coil", _ => "guides",
    });
}

/// <summary>Split objective attackers across the field, countering critter armies.</summary>
internal sealed class VerminionBattleStrategy(int stage = 2)
{
    public static bool IsStoneStage(int stage) => stage is 2 or 3 or 5 or 7 or 8 or
        10 or 11 or 13 or 14 or 16 or 17 or 18 or 20 or 21 or 22;
    public const ushort Minion = 2; // Mammet #001: ordinary city vendor, Arcana strength.
    public const string MinionName = "Mammet #001";
    public const ushort CritterCounter = 26; // Baby Bat: ordinary vendor, monster, speed 4.
    public const string CritterCounterName = "Baby Bat";
    public enum Action { Wait, SelectGate, Summon, AttackObjective }
    private int lane;
    private int summons;
    public VerminionSummonTracker Reinforcements { get; } = new();
    public bool OpeningComplete => lane >= 3;
    public bool HasDeployedGroup => lane > 0;
    public int Gate => (lane % 3) switch { 0 => 1, 1 => 0, _ => 2 };
    public int EnemyLane => 2 - Gate; // Enemy A/C labels are mirrored across the field.
    public ushort CurrentMinion => VerminionRoster.Attacker(stage).Id;
    public string CurrentMinionName => VerminionRoster.Attacker(stage).Name;
    public int GroupSize => 60 / MinionCost;
    public int MinionCost => VerminionRoster.Attacker(stage).Cost;
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
    public const ushort Minion = 52; // Wind-up Airship: level 15 main scenario reward.
    public const string MinionName = "Wind-up Airship";
    public const ushort DefenderMinion = 26;
    public const string DefenderName = "Baby Bat";
    public const int DefenderCost = 10;
    public const int DefenderCount = 4;
    public ushort CurrentDefenderMinion => stage == 20 ? VerminionRoster.Hatchling.Id : DefenderMinion;
    public string CurrentDefenderName => stage == 20 ? VerminionRoster.Hatchling.Name : DefenderName;
    public int CurrentDefenderCost => stage == 20 ? VerminionRoster.Hatchling.Cost : DefenderCost;
    public int CurrentDefenderCount => VerminionRoster.HasDefenders(stage) ? DefenderCount : 0;
    public bool DefendsStone => stage is 12 or 19;
    public ushort CurrentMinion => VerminionRoster.Attacker(stage).Id;
    public string CurrentMinionName => VerminionRoster.Attacker(stage).Name;
    public int MinionCost => VerminionRoster.Attacker(stage).Cost;
    public int WaveSize => MinionCost > 10 ? 4 : 6;
    public int ArmySize => 240 / MinionCost;
    // Gentleman withdraws the four casters. Never trade the last attack party
    // for a buff with no surviving army, or spend it before the final burn.
    // The final stage needs living units for towers; its guide does not call
    // for sacrificing an action party. Keep that special disabled there.
    public bool ShouldUseSpecial(uint bossHp, uint bossMaxHp, int livingUnits) => stage != 24 &&
        (stage != 23 || bossMaxHp > 0 && bossHp > 0 && (ulong)bossHp * 5 <= bossMaxHp && livingUnits >= 8);
    public VerminionSummonTracker Defenders { get; } = new();
    public int AttackCapacity(int capacity, int defenders) => capacity <= 60 ? capacity :
        System.Math.Max(0, capacity - System.Math.Max(0, CurrentDefenderCount - defenders) * CurrentDefenderCost);
    public bool CanRequestDefender(int defenders, int usedCapacity, int capacity) =>
        defenders + Defenders.Pending < CurrentDefenderCount &&
        Defenders.CanRequest(usedCapacity + PendingSummons * MinionCost, capacity, CurrentDefenderCost);
    public static bool IsInvulnerabilityAdd(int stage, string name) => stage == 6 &&
        (name.Equals("Infant Imp", System.StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Imp", System.StringComparison.OrdinalIgnoreCase));
    public static bool IsFinalCoilBoss(string name) =>
        name.Equals("Wind-up Bahamut", System.StringComparison.OrdinalIgnoreCase);
    private ulong finalCoilAddTarget;
    private bool finalCoilAddDefeated;

    public ulong ChooseFinalCoilTarget(ulong bossId, IEnumerable<(ulong Id, string Name, uint Hp)> enemies)
    {
        if (stage != 24 || bossId == 0 || finalCoilAddDefeated) return bossId;
        var adds = enemies.Where(enemy => enemy.Id != 0 && enemy.Id != bossId &&
            enemy.Name.Equals("Clockwork Twintania", System.StringComparison.OrdinalIgnoreCase)).ToArray();
        if (finalCoilAddTarget == 0)
        {
            var living = adds.Where(add => add.Hp > 0).OrderBy(add => add.Hp).ThenBy(add => add.Id).ToArray();
            // A single visible add is insufficient to start the two-add phase.
            // This also leaves a surviving add alone after a mid-battle reload.
            if (living.Length < 2) return bossId;
            finalCoilAddTarget = living[0].Id;
        }
        var target = adds.FirstOrDefault(add => add.Id == finalCoilAddTarget);
        // Missing from this snapshot does not prove a kill and must not select
        // a different add. Return to Bahamut; resume this same add if it returns.
        if (target.Id == 0) return bossId;
        if (target.Hp > 0) return target.Id;
        finalCoilAddDefeated = true;
        return bossId;
    }

    public enum Action { Wait, SelectGate, Summon, SendWave, FollowBoss }
    private readonly VerminionSummonTracker summons = new();
    public int PendingSummons => summons.Pending;
    public bool WaitingForWave => PendingSummons > 0;
    public int ObserveUnits(System.Collections.Generic.IEnumerable<ulong> units) => summons.ObserveUnits(units);

    public Action Decide(bool gateSelected, int readyUnits, int deployedUnits,
        int usedCapacity, int capacity, double waveSeconds, double orderSeconds, bool bossMoved, bool gateAvailable = true, bool assemble = false)
    {
        // Selection and camera work can take several seconds. Keep two requests
        // in flight before starting another final-boss movement sequence, instead
        // of starving production whenever the boss takes another step.
        if (stage is 23 or 24 && gateAvailable && PendingSummons < 2 && summons.CanRequest(usedCapacity, capacity, MinionCost))
            return gateSelected ? Action.Summon : Action.SelectGate;
        if (!assemble && (readyUnits >= WaveSize || readyUnits > 0 && waveSeconds >= 60)) return Action.SendWave;
        // Both final stages need their replacements in combat promptly. Do not
        // leave the last minion idle at capacity or wait for four survivors.
        if (stage is 23 or 24 && !assemble && readyUnits > 0 &&
            (waveSeconds >= 15 || PendingSummons == 0 && usedCapacity + MinionCost > capacity))
            return Action.SendWave;
        if (stage is 23 or 24 && !assemble && deployedUnits > 0 && orderSeconds >= 5 && bossMoved)
            return Action.FollowBoss;
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
