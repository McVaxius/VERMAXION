using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace VERMAXION.Services;

internal sealed record VerminionMinion(ushort Id, string Name, int Cost, string Acquisition);
internal readonly record struct VerminionGroundOmen(int Slot, bool Tower, Vector3 Position, float Radius);
// Battle-local observations follow native warning commands, not render-object lifetime.
internal sealed class VerminionGroundWarnings
{
    private readonly Dictionary<int, VerminionGroundOmen?> active = new();
    public bool HasUnreadableWarning => active.Values.Any(omen => omen == null);
    public IReadOnlyList<VerminionGroundOmen> Omens => active.OrderBy(entry => entry.Key)
        .Where(entry => entry.Value != null).Select(entry => entry.Value!.Value).ToArray();

    public void Command(int slot, bool created)
    {
        if (slot is < 11 or >= 30) return;
        if (created) active[slot] = null; // A replacement must never reuse old geometry.
        else active.Remove(slot);
    }

    public void ObserveSnapshot(IReadOnlyList<VerminionGroundOmen> omens, bool seedExisting = false)
    {
        foreach (var omen in omens)
            if (omen.Slot is >= 11 and < 30 && (seedExisting || active.ContainsKey(omen.Slot)))
                active[omen.Slot] = omen;
        // Empty or incomplete visual snapshots cannot prove native removal.
    }

    public void Clear() => active.Clear();
}
internal sealed record VerminionVendorMinion(ushort MinionId, uint ItemId, string Name, uint Gil, uint Mgp = 0, uint Certificates = 0)
{
    public uint Price => Certificates > 0 ? Certificates : Mgp > 0 ? Mgp : Gil;
    public string Currency => Certificates > 0 ? "Achievement Certificates" : Mgp > 0 ? "MGP" : "gil";
    public string CurrencyKind => Certificates > 0 ? "CurrencyManager" : Mgp > 0 ? "Mgp" : "Gil";
    public uint CurrencyItemId => Certificates > 0 ? 21172u : Mgp > 0 ? 29u : 1u;
}

/// <summary>Accessible baseline choices. Guide adaptations still require live verification.</summary>
internal static class VerminionRoster
{
    internal static readonly ushort[] GentlemanQuests =
    [1204, 1205, 1206, 1207, 1315, 1316, 1317, 1318, 1438, 1439, 1440, 1441, 166, 202, 203, 204, 490, 491, 492, 493, 502];
    public static ushort[] AcquisitionQuests(ushort minion) => minion == 21 ? GentlemanQuests : [];
    public static readonly VerminionVendorMinion MammetOffer = new(2, 6004, "Mammet #001", 2400);
    public static readonly VerminionVendorMinion HatchlingOffer = new(3, 6005, "Wayward Hatchling", 2400);
    // Retained only to reconcile any receipt saved before the baseline roster changed.
    public static readonly VerminionVendorMinion BatOffer = new(26, 6187, "Baby Bat", 2400);
    public static readonly VerminionVendorMinion NeroOffer = new(174, 14096, "Wind-up Nero tol Scaeva", 0, 30000);
    public static readonly VerminionVendorMinion ZuOffer = new(83, 7565, "Zu Hatchling", 0, 10000);
    public static readonly VerminionVendorMinion[] AchievementOffers =
    [new(76, 7561, "Wind-up Odin", 0, 0, 2), new(51, 6212, "Wind-up Cursor", 0, 0, 2)];
    private static readonly VerminionVendorMinion[] EntryOffers =
    [
        MammetOffer,
        HatchlingOffer,
        new(1, 6003, "Cherry Bomb", 2400),
    ];

    public static VerminionVendorMinion? VendorOffer(uint itemId, ushort minionId) =>
        VendorOffer(minionId) is { } offer && offer.ItemId == itemId ? offer : null;

    public static VerminionVendorMinion? VendorOffer(ushort minionId) =>
        EntryOffers.Append(BatOffer).Append(NeroOffer).Append(ZuOffer).Concat(AchievementOffers).FirstOrDefault(offer => offer.MinionId == minionId);

    // Prefer already registered minions, then buy only the distinct minions
    // needed for entry. Mammet also supplies the ordinary-stage battle roster.
    public static VerminionVendorMinion[] EntryPurchases(IEnumerable<ushort> owned)
    {
        var registered = owned.Where(id => id != 0).ToHashSet();
        return EntryOffers.Where(offer => !registered.Contains(offer.MinionId))
            .Take(System.Math.Max(0, 3 - registered.Count)).ToArray();
    }

    public static readonly VerminionMinion Mammet = new(2, "Mammet #001", 10,
        "Minion Trader, Minion Square: 2,400 gil.");
    public static readonly VerminionMinion Hatchling = new(3, "Wayward Hatchling", 15,
        "Minion Trader, Minion Square: 2,400 gil.");
    public static readonly VerminionMinion Airship = new(52, "Wind-up Airship", 25,
        "Reward from your starting city's level 15 Envoy main scenario quest.");
    public static readonly VerminionMinion Nero = new(174, "Wind-up Nero tol Scaeva", 20,
        "30,000 MGP from the Minion Trader in Minion Square; permanent vendor stock with no additional unlock.");
    public static readonly VerminionMinion Zu = new(83, "Zu Hatchling", 10,
        "Minion Trader, Minion Square: 10,000 MGP, permanent stock.");
    public static readonly VerminionMinion Gentleman = new(21, "Wind-up Gentleman", 30,
        "Reward from Her Last Vow, the final level 50 ARR Hildibrand quest.");

    public static VerminionMinion Attacker(int stage) => stage == 7 ? Zu :
        stage is 12 or 15 or 23 or 24 ? Gentleman : VerminionBattleStrategy.IsStoneStage(stage) ? Mammet : Airship;
    public static bool HasDefenders(int stage) => stage is 6 or 16 or 20;
    public static VerminionMinion[] Required(int stage) => stage is < 2 or > 24 ? [] :
        HasDefenders(stage) ? [Attacker(stage), stage == 20 ? Hatchling : Zu] : [Attacker(stage)];

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
        20 => "Six Mammets per lane attack stones. Four Wayward Hatchlings counter poppets at the center stone, then defend a surviving stone. Reserve 60 capacity for the defenders and use Choco Shuffle near enemies. This vendor adaptation cleared on its first fresh mixed-opening attempt, including defender summons and their special; repeated-win reliability is not established.",
        7 => "Zu Hatchlings counter critters with area auto-attacks and Nasty Peck's attack boost. Split across stones and reinforce. This permanent-vendor composition cleared on its third attempt after two defeats; repeated-win reliability is not established.",
        16 => "Follow the guide's Mammet stone-attack option: six Mammets per lane exploit their Arcana strength. Reserve 40 capacity for four Zu Hatchlings to counter critters near a surviving friendly stone, using Nasty Peck's attack boost. This permanent-vendor composition cleared on its first attempt after three Zu-only defeats. Repeated-win reliability is not established; no FATE minion is required.",
        11 => "Mammet groups split across stones. The guide recommends extra combat support here; this vendor-only composition has one fresh live clear.",
        22 => "Split Mammet groups across stones, redirect survivors and replace losses. The guide suggests Wind-up Odin support; the Mammet-only baseline has one observed clear.",
        4 => "Airships follow the boss between stones; group four to use Cargo's area ATK buff. Replace casualties and avoid wasting orders while attacking.",
        6 => "Airships switch to the Imps that maintain invulnerability, then return to the boss. Four Zu Hatchlings defend the center stone. This permanent-vendor composition has one observed live clear.",
        9 => "Fast Airships carry bombs from the lever, place and arm them by the invulnerable slime, then attack the vulnerable final phase.",
        12 => "Follow the linked Gentleman-only guide: send Wind-up Gentlemen to Demon Brick as they spawn, pursue from behind and ignore other minions. Use surviving gates for replacements. Keep the withdrawing special disabled. This composition cleared on its third attempt after two defeats; repeated-win reliability is not established.",
        15 => "Use Wind-up Gentlemen, following the linked Gentleman guide. Assemble eight attackers, follow Odin into melee and replace losses; recover the army before the final phase. At 25% HP, use Power of Deduction only with at least eight living attackers because it withdraws its four-minion action party. Revised pursuit still needs live verification.",
        19 => "Up to nine Airships defend the center stone and intercept Enkidu. Do not chase Gilgamesh away; retreat to heal during his ATK/DEF/SPD buff and return afterward. Use Cargo's area ATK buff on the group.",
        23 => "Wind-up Gentlemen rush Twintania as they spawn. Keep replacements queued and ignore resistant nodes. Save Power of Deduction for the final 20% while eight attackers survive: it buffs nearby allies but withdraws its four-minion action party. This published composition cleared on its first attempt on the current test character; repeated-win reliability is not established. The tested Airship, Nero and Zu substitutions failed.",
        24 => "Follow the guide's Wind-up Gentleman composition: dodge plain red circles, send one healthy Gentleman into each pillar circle, focus one Clockwork Twintania when both adds appear, then return to Bahamut. Reinforce losses and keep the withdrawing special disabled. This composition has one observed live clear; repeated-win reliability remains unverified.",
        _ => "No challenge selected.",
    };

    public static string? AchievementSupport(int stage) => stage switch
    {
        16 or 22 => "The linked guide recommends two Wind-up Odin to support stone attackers. Odin is a Monster with speed 4, 500 HP, 75 ATK and summon cost 30; its special deals 90 potency area damage. This support composition has not been tested by the bot.",
        19 => "The linked guide's first strategy includes four Wind-up Odin alongside healers and defenders. Odin costs 30 capacity and has speed 4. The bot currently uses its separately tested Airship defense strategy.",
        23 => "The linked alternate guide uses four Wind-up Cursor and six Wind-up Gentlemen. Cursor is a Gadget, costs 15 capacity and reduces nearby enemies' DEF by 50% for 15 seconds. This mixed composition has not been tested by the bot.",
        24 => "The linked guide allows White Mage Minions of Light instead of Louisoix, but describes this as harder. They cost 10 capacity, have speed 3 and heal nearby allies for 100 HP (150 for poppets). The shared Minion of Light item costs two certificates; selection of its White Mage form still needs verification.",
        _ => null,
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
    public const ushort CritterCounter = 83; // Zu Hatchling: permanent MGP vendor, monster, speed 3.
    public const string CritterCounterName = "Zu Hatchling";
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
    public const ushort DefenderMinion = 83;
    public const string DefenderName = "Zu Hatchling";
    public const int DefenderCost = 10;
    public const int DefenderCount = 4;
    public ushort CurrentDefenderMinion => stage == 20 ? VerminionRoster.Hatchling.Id : DefenderMinion;
    public string CurrentDefenderName => stage == 20 ? VerminionRoster.Hatchling.Name : DefenderName;
    public int CurrentDefenderCost => stage == 20 ? VerminionRoster.Hatchling.Cost : DefenderCost;
    public int CurrentDefenderCount => VerminionRoster.HasDefenders(stage) ? DefenderCount : 0;
    public bool DefendsStone => stage == 19;
    public ushort CurrentMinion => VerminionRoster.Attacker(stage).Id;
    public string CurrentMinionName => VerminionRoster.Attacker(stage).Name;
    public int MinionCost => VerminionRoster.Attacker(stage).Cost;
    public int WaveSize => MinionCost > 10 ? 4 : 6;
    public int ArmySize => 240 / MinionCost;
    // Gentleman withdraws the four casters. Never trade the last attack party
    // for a buff with no surviving army, or spend it before the final burn.
    // Demon Brick needs sustained pursuit, and Bahamut needs units for towers;
    // neither guide calls for sacrificing an action party.
    public bool ShouldUseSpecial(uint bossHp, uint bossMaxHp, int livingUnits) => stage is not (12 or 24) &&
        (stage is not (15 or 23) || bossMaxHp > 0 && bossHp > 0 &&
            (ulong)bossHp * (stage == 15 ? 4u : 5u) <= bossMaxHp && livingUnits >= 8);
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

    public static Vector3? FinalCoilDodgeDestination(VerminionGroundOmen circle, Vector3 attackTarget,
        IReadOnlyList<VerminionGroundOmen>? circles = null)
    {
        // Native scale and the paired image locate the circle, but its exact
        // damage boundary is unmeasured. Add clearance for the selected party,
        // staying near its attack target rather than kiting it across the board.
        var distance = circle.Radius + 3;
        return Enumerable.Range(0, 16).Select(index =>
                circle.Position + new Vector3(System.MathF.Sin(index * System.MathF.PI / 8), 0,
                    System.MathF.Cos(index * System.MathF.PI / 8)) * distance)
            .Where(point => System.MathF.Abs(point.X) <= 23 && System.MathF.Abs(point.Z) <= 23)
            .Where(point => circles == null || circles.All(other => other.Tower ||
                Vector3.DistanceSquared(point, other.Position) > (other.Radius + 1) * (other.Radius + 1)))
            // Accepted omens have radius <= 10 and centers inside this board;
            // at least one sampled direction remains on the board. Overlapping
            // warnings may exclude it; never use a default unsafe destination.
            .OrderBy(point => Vector3.DistanceSquared(point, attackTarget))
            .Select(point => (Vector3?)point).FirstOrDefault();
    }

    public static bool CanContinueFinalCoilTowerOrder(IReadOnlyList<VerminionGroundOmen> omens,
        Vector3 origin, Vector3 destination)
    {
        if (!omens.Any(omen => omen.Tower && Vector3.DistanceSquared(omen.Position, destination) <= 1)) return false;
        var route = destination - origin;
        foreach (var circle in omens.Where(omen => !omen.Tower))
        {
            var along = route.LengthSquared() == 0 ? 0 :
                System.Math.Clamp(Vector3.Dot(circle.Position - origin, route) / route.LengthSquared(), 0, 1);
            if (Vector3.DistanceSquared(origin + along * route, circle.Position) <=
                (circle.Radius + 1) * (circle.Radius + 1)) return false;
        }
        return true;
    }

    public static bool CanContinueFinalCoilDodgeOrder(IReadOnlyList<VerminionGroundOmen> omens, Vector3 destination)
        => omens.Any(omen => !omen.Tower) && omens.Where(omen => !omen.Tower).All(circle =>
            Vector3.DistanceSquared(destination, circle.Position) > (circle.Radius + 1) * (circle.Radius + 1));

    public static ulong ChooseFinalCoilTowerOccupant(
        IEnumerable<(ulong Id, Vector3 Position, uint Hp, uint MaxHp)> units,
        Vector3 tower, ulong assigned, bool orderSent)
    {
        var living = units.Where(unit => unit.Id != 0 && unit.Hp > 0 && unit.MaxHp > 0).ToArray();
        var current = living.FirstOrDefault(unit => unit.Id == assigned);
        // Keep an occupant already absorbing the tower, or still travelling on
        // its verified order. Once a dodge interrupts that order, a wounded unit
        // outside the tower must not prevent selecting a healthy replacement.
        if (current.Id != 0 && (orderSent || current.Hp >= current.MaxHp / 2 ||
                Vector3.DistanceSquared(current.Position, tower) <= 1)) return current.Id;
        return living.Where(unit => unit.Hp >= unit.MaxHp / 2)
            .OrderBy(unit => Vector3.DistanceSquared(unit.Position, tower))
            .ThenByDescending(unit => unit.Hp).Select(unit => unit.Id).FirstOrDefault();
    }

    private ulong finalCoilAddTarget;
    private bool finalCoilAddDefeated;
    private readonly Dictionary<ulong, System.DateTime> finalCoilAttackOrders = new();

    public bool CanOrderFinalCoilAttacker(ulong unit, Vector3 position, Vector3 target,
        ulong towerOccupant, System.DateTime now) => stage == 24 && towerOccupant != 0 &&
        unit != 0 && unit != towerOccupant && Vector3.DistanceSquared(position, target) >= 4 &&
        (!finalCoilAttackOrders.TryGetValue(unit, out var sent) || (now - sent).TotalSeconds >= 10);

    public ulong ChooseFinalCoilAttacker(IEnumerable<(ulong Id, Vector3 Position)> units,
        Vector3 target, ulong towerOccupant, System.DateTime now)
    {
        return units.Where(unit => CanOrderFinalCoilAttacker(unit.Id, unit.Position, target, towerOccupant, now))
            .OrderBy(unit => Vector3.DistanceSquared(unit.Position, target))
            .Select(unit => unit.Id).FirstOrDefault();
    }

    public void FinalCoilAttackerDispatched(ulong unit, System.DateTime now)
    {
        if (stage == 24 && unit != 0) finalCoilAttackOrders[unit] = now;
    }

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

    public static Vector3 DemonBrickDestination(Vector3 boss, float rotation, Vector3? anchor)
    {
        var facing = new Vector3(System.MathF.Sin(rotation), 0, System.MathF.Cos(rotation));
        if (anchor is { } position && Vector3.Dot(position - boss, facing) > 0)
        {
            var side = new Vector3(facing.Z, 0, -facing.X);
            var sign = Vector3.Dot(position - boss, side) < 0 ? -1 : 1;
            return boss + side * sign * 2 - facing * 2;
        }
        // This rear destination must remain safe on the next decision. A -1
        // threshold would send a unit at the desired -0.8 point on a new detour.
        return boss - facing * 0.8f;
    }

    public ulong ChooseStraggler((ulong Id, System.Numerics.Vector3 Position)[] units, System.Numerics.Vector3 target)
    {
        if (stage is not (12 or 24)) return 0;
        // A dispatched group order does not prove every minion was selected.
        // Rejoin an observed attacking group without redirecting an army that
        // is still travelling together toward a moving boss.
        var engaged = units.Where(unit => unit.Id != 0 &&
                System.Numerics.Vector3.DistanceSquared(unit.Position, target) < 36)
            .OrderByDescending(unit => units.Count(other =>
                System.Numerics.Vector3.DistanceSquared(unit.Position, other.Position) < 25))
            .FirstOrDefault();
        if (engaged.Id == 0) return 0;
        var separationSquared = stage == 24 ? 36 : 100;
        return units.Where(unit => unit.Id != 0 &&
                System.Numerics.Vector3.DistanceSquared(unit.Position, engaged.Position) >= separationSquared &&
                System.Numerics.Vector3.DistanceSquared(unit.Position, target) >= separationSquared)
            .OrderByDescending(unit => units.Count(other =>
                System.Numerics.Vector3.DistanceSquared(unit.Position, other.Position) < 25))
            .ThenBy(unit => System.Numerics.Vector3.DistanceSquared(unit.Position, target))
            .Select(unit => unit.Id).FirstOrDefault();
    }

    public ulong ChooseOdinRecoveryUnit((ulong Id, Vector3 Position)[] units, Vector3 gate)
    {
        // Let the main party retreat first, then collect units its selection missed.
        if (stage != 15 || !units.Any(unit => unit.Id != 0 && Vector3.DistanceSquared(unit.Position, gate) < 36)) return 0;
        return units.Where(unit => unit.Id != 0 && Vector3.DistanceSquared(unit.Position, gate) >= 36)
            .OrderByDescending(unit => Vector3.DistanceSquared(unit.Position, gate))
            .Select(unit => unit.Id).FirstOrDefault();
    }

    public static bool OdinArmyInMelee(IEnumerable<Vector3> units, Vector3 boss)
    {
        var positions = units.ToArray();
        // One selected attacker in range does not establish the army's arrival.
        return positions.Length > 0 && positions.Count(position => Vector3.DistanceSquared(position, boss) < 4) >=
            (positions.Length + 1) / 2;
    }

    public Action Decide(bool gateSelected, int readyUnits, int deployedUnits,
        int usedCapacity, int capacity, double waveSeconds, double orderSeconds, bool bossMoved, bool gateAvailable = true, bool assemble = false)
    {
        // Selection and camera work can take several seconds. Keep two requests
        // in flight before starting another boss-rush movement sequence, instead
        // of starving production whenever the boss takes another step.
        if (stage is 12 or 15 or 23 or 24 && gateAvailable && PendingSummons < 2 && summons.CanRequest(usedCapacity, capacity, MinionCost))
            return gateSelected ? Action.Summon : Action.SelectGate;
        if (!assemble && (readyUnits >= WaveSize || readyUnits > 0 && waveSeconds >= 60)) return Action.SendWave;
        // These boss rushes need their replacements in combat promptly. Do not
        // leave the last minion idle at capacity or wait for four survivors.
        if (stage is 12 or 15 or 23 or 24 && !assemble && readyUnits > 0 &&
            (waveSeconds >= 15 || PendingSummons == 0 && usedCapacity + MinionCost > capacity))
            return Action.SendWave;
        if (stage is 12 or 15 or 23 or 24 && !assemble && deployedUnits > 0 && orderSeconds >= 5 && bossMoved)
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
