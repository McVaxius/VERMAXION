// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f. Private local port.
using ECommons.GameHelpers;

using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;

using Lumina.Excel.Sheets.Experimental;
using Newtonsoft.Json;
#pragma warning disable PendingExcelSchema

namespace VERMAXION.Beastmaster;

using ECommons;
using ECommons.Throttlers;
using VERMAXION.Beastmaster.Readers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Screens = CrucibleUi.Screens;

public partial record CrucibleFamiliar
{
    [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
    public CrucibleFamiliarSheetData SheetData => Number > 0 && CrucibleTeam.FamiliarSheetDatas.TryGetValue(Number, out var sheet) ? sheet : CrucibleFamiliarSheetData.invalid;
    public int Score() { var index = BeastmasterRuntime.ActiveTierList.FindIndex(x => x.ID == Number); return index < 0 ? int.MaxValue : index; }
}
public record CrucibleFamiliarSheetData
{
    public required     uint   Number             { get; init; }
    public required     string Name               { get; init; }
    public required     string Element            { get; set; }
    public required     byte   Classification     { get; init;}
    [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
    public string ClassificationName => this.Classification < CrucibleTeam.ClassificationSheetNames.Length ? CrucibleTeam.ClassificationSheetNames[this.Classification] : string.Empty;

    public static readonly CrucibleFamiliarSheetData invalid = new()
                                                               {
                                                                   Number         = 0,
                                                                   Name           = string.Empty,
                                                                   Element        = string.Empty,
                                                                   Classification = 0
                                                               };
}

internal static unsafe class CrucibleTeam
{
    public static int TeamSize(uint? id = null, CrucibleLevelingMode? levelingMode = null)
    {
        int size = BeastmasterSelection.TeamCapacity(id ?? BeastmasterRuntime.SelectedTerritory);


        BeastmasterSettings crucible = BeastmasterRuntime.Settings;

        if (BeastmasterSelection.RequiresClearTeam(crucible)) return size;

        return crucible.TeamMode switch
        {
            CrucibleTeamMode.Recommended => Math.Min(size, crucible.RecommendedTeamCap),
            CrucibleTeamMode.Leveling => (levelingMode ?? crucible.LevelingMode) switch
            {
                CrucibleLevelingMode.Minus_3 => size - 3,
                CrucibleLevelingMode.Only_3 => 3,
                CrucibleLevelingMode.Full => size,
                CrucibleLevelingMode.Carry_Minus_2 => size - 2,
                _ => throw new ArgumentOutOfRangeException(nameof(levelingMode))
            },
            _ => size
        };
    }


    public const int FightSize = 3;

    private static readonly string[] DetailWindows = ["XBMMonsterBookDetail", "XBMPetActionDetail"];
    private static readonly TimeSpan CacheInterval = TimeSpan.FromMilliseconds(750);

    public static SortedDictionary<uint, CrucibleFamiliar> FamiliarsFallback => field ??= new SortedDictionary<uint, CrucibleFamiliar>(FamiliarSheetDatas.Keys.ToDictionary(key => key, key => new CrucibleFamiliar(key)));

    public static SortedDictionary<uint, CrucibleFamiliarSheetData> FamiliarSheetDatas
    {
        get
        {
            if (field != null)
                return field;

            SortedDictionary<uint, CrucibleFamiliarSheetData> familiars = [];

            foreach (XBMPet familiar in Svc.Data.GetExcelSheet<XBMPet>())
                if (familiar.RowId > 0 && familiar.Pet.ValueNullable?.Name.ExtractText() is { Length: > 0 } name)
                    familiars[familiar.RowId] = new CrucibleFamiliarSheetData
                                                {
                                                    Number         = familiar.RowId,
                                                    Name           = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name),
                                                    Element        = "", //familiar.Element.Value?.Name.ExtractText() ?? string.Empty,
                                                    Classification = familiar.Classification
                                                };

            return field = familiars;
        }
    }


    public static string[] ClassificationSheetNames
    {
        get
        {
            if (field != null)
                return field;

            try
            {
                byte     maxIds = Svc.Data.GetExcelSheet<RawRow>(name: "XBMPet").Max(p => p.ReadUInt8(24));
                string[] names  = new string[maxIds+1];

                names[0] = string.Empty;
                for (uint i = 1; i <= maxIds; i++)
                    names[i] = Svc.Data.GetExcelSheet<Addon>().GetRow(17740 + i).Text.ExtractText();

                return field = names;
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "[Crucible] Couldn't read the XBMPet sheet; familiar names are unknown");
            }

            return [];
        }
    }

    private static SortedDictionary<uint, string> SheetNames
    {
        get
        {
            if (field != null)
                return field;

            SortedDictionary<uint, string> names = [];
            try
            {
                ExcelSheet<Pet> pets = Svc.Data.GetExcelSheet<Pet>();
                foreach (RawRow familiar in Svc.Data.GetExcelSheet<RawRow>(name: "XBMPet"))
                    if (familiar.RowId > 0 && pets.TryGetRow((uint)familiar.ReadInt32Column(0), out Pet pet) && pet.Name.ExtractText() is { Length: > 0 } name)
                        names[familiar.RowId] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "[Crucible] Couldn't read the XBMPet sheet; familiar names are unknown");
                names.Clear();
            }

            return field = names;
        }
    }

    private static DateTime nextCacheRead;

    private static BeastmasterCharacterData? Mine(bool create)
    {
        if (!Player.Available) return null;
        var mine = BeastmasterRuntime.CharacterData;
        if (create) mine.EnsureFamiliars(FamiliarSheetDatas.Keys);
        return mine;
    }

    public static void ClearSaved()
    {
        if (!Player.Available) return;
        BeastmasterRuntime.CharacterData.Familiars.Clear();
        BeastmasterRuntime.CharacterData.Team.Clear();
        BeastmasterRuntime.CharacterData.Owned.Clear();
        BeastmasterRuntime.CharacterData.OwnershipObservedAtUtc = DateTime.MinValue;
        BeastmasterRuntime.CharacterData.RanksObservedAtUtc = DateTime.MinValue;
        BeastmasterRuntime.Save();
    }
    public static IReadOnlyDictionary<uint, CrucibleFamiliar> Familiars =>
        Mine(false)?.Familiars ?? FamiliarsFallback;

    public static IReadOnlyList<uint> CurrentTeam =>
        Mine(false)?.Team ?? [];

    public static string NameOf(uint number) => Familiars.TryGetValue(number, out CrucibleFamiliar? seen) && seen.SheetData.Name.Length > 0 ? 
                                                    seen.SheetData.Name : 
                                                    SheetNames.TryGetValue(number, out string? name) ? name : $"No. {number}";

    public static uint NumberFor(string name)
    {
        if (name.Length == 0)
            return 0;

        CrucibleFamiliar? seen = Familiars.Values.FirstOrDefault(x => string.Equals(x.SheetData.Name, name, StringComparison.OrdinalIgnoreCase));
            
        return seen is { SheetData.Number: > 0 } ? seen.SheetData.Number : SheetNames.FirstOrDefault(x => string.Equals(x.Value, name, StringComparison.OrdinalIgnoreCase)).Key;
    }

    public static IEnumerable<uint> Owned()
    {
        if (BeastmasterRuntime.Service.PreparedOwned is { } prepared) return prepared;
        XBMManager*                                 manager = XBMManager.Instance();
        bool                                        ready   = manager != null && manager->State == XBMManager.DataState.Received;
        IReadOnlyDictionary<uint, CrucibleFamiliar> cached  = Familiars;
        return SheetNames.Keys.Where(x => ready ? manager->IsPetUnlocked(x) : cached.TryGetValue(x, out var familiar) && familiar.Rank > 0);
    }

    public static bool TryObserveOwnership(BeastmasterCharacterData data, out HashSet<uint> owned)
    {
        owned = [];
        XBMManager* manager = XBMManager.Instance();
        if (manager == null || manager->State != XBMManager.DataState.Received) return false;
        owned = FamiliarSheetDatas.Keys.Where(number => manager->IsPetUnlocked(number)).ToHashSet();
        data.Owned = [..owned];
        data.OwnershipObservedAtUtc = DateTime.UtcNow;
        return true;
    }

    public static bool OwnershipKnown
    {
        get
        {
            XBMManager* manager = XBMManager.Instance();
            return manager != null && manager->State == XBMManager.DataState.Received;
        }
    }

    public static List<uint> For(CrucibleTeamMode mode, bool sizeCap = true) => BeastmasterSelection.RequiresClearTeam(BeastmasterRuntime.Settings)
        ? BeastmasterSelection.ClearRoster(BeastmasterRuntime.Settings.Board)
        : mode switch
    {
        CrucibleTeamMode.Leveling    => Leveling(sizeCap),
        CrucibleTeamMode.Recommended => Recommended(sizeCap),
        _                            => Custom()
    };

    public static List<uint> Custom()
    {
        BeastmasterSettings crucible = BeastmasterRuntime.Settings;

        HashSet<uint> owned = Owned().ToHashSet();
        return crucible.CustomTeam.Distinct().Where(owned.Contains)
            .Where(x => FamiliarSheetDatas.ContainsKey(x) && (crucible.ClassificationLock == 0 || FamiliarSheetDatas[x].Classification == crucible.ClassificationLock))
            .Take(TeamSize()).ToList();
    }

    public static bool SetCustomPick(uint number, bool pick)
    {
        List<uint> team = Custom();
        if (pick)
        {
            if (team.Contains(number) || team.Count >= TeamSize() || !Owned().Contains(number))
                return false;
            team.Add(number);
        }
        else if (!team.Remove(number))
        {
            return false;
        }

        SaveCustom(team);
        return true;
    }

    private static void SaveCustom(List<uint> team)
    {
        if (!OwnershipKnown)
            team = team.Concat(BeastmasterRuntime.Settings.CustomTeam.Where(x => !team.Contains(x))).Distinct().Take(TeamSize()).ToList();

        BeastmasterRuntime.Settings.CustomTeam = team;
        BeastmasterRuntime.Save();
    }

    public static List<uint> Recommended(bool sizeCap)
    {
        BeastmasterSettings crucible = BeastmasterRuntime.Settings;
        return BeastmasterSelection.Recommended(Owned().Select(Candidate), sizeCap ? TeamSize() : int.MaxValue, crucible.ClassificationLock);
    }

    public static List<uint> Leveling(bool sizeCap)
    {
        BeastmasterSettings crucible     = BeastmasterRuntime.Settings;
        CrucibleLevelingMode                             levelingMode = crucible.LevelingMode;

        return crucible.Goal == BeastmasterRunGoal.TrainOwned
            ? BeastmasterSelection.Training(Owned().Select(Candidate), sizeCap ? TeamSize() : int.MaxValue,
                levelingMode == CrucibleLevelingMode.Carry_Minus_2, BeastmasterSelection.TrainingTarget(crucible), crucible.ClassificationLock)
            : BeastmasterSelection.Leveling(Owned().Select(Candidate), sizeCap ? TeamSize() : int.MaxValue,
                levelingMode == CrucibleLevelingMode.Carry_Minus_2, crucible.ClassificationLock);
    }

    public static bool TryPrepareSelection(out List<uint> team, out string reason)
    {
        var settings = BeastmasterRuntime.Settings;
        var candidates = Owned().Where(FamiliarSheetDatas.ContainsKey).Select(Candidate).ToArray();
        var capacity = TeamSize();
        if (BeastmasterSelection.TryPrepareTeam(settings, candidates, capacity, out team, out reason)) return true;
        if (!BeastmasterSelection.TryRecoverExcludedTrainees(settings, candidates, capacity, out var rebuilt)) return false;
        team = rebuilt;
        reason = string.Empty;
        Svc.Log.Warning("[Crewcible] Training selection failed because unfinished beasts were excluded; temporarily allowing those beasts for this run and rebuilding the team.");
        return true;
    }

    private static BeastmasterCandidate Candidate(uint number)
    {
        Familiars.TryGetValue(number, out var familiar);
        var tier = BeastmasterRuntime.ActiveTierList.FirstOrDefault(e => e.ID == number);
        return new BeastmasterCandidate(number, familiar?.Rank ?? 0,
            string.IsNullOrEmpty(familiar?.Exp) ? float.NaN : CrucibleUi.ExpShare(familiar.Exp),
            familiar?.Score() ?? int.MaxValue, FamiliarSheetDatas.TryGetValue(number, out var sheet) ? sheet.Classification : (byte)0,
            tier?.PickBlock ?? false, tier?.BattleBlock ?? false);
    }

    public static (int Rank, float Exp) LevelingKey(uint number, int liveRank = 0)
    {
        Familiars.TryGetValue(number, out CrucibleFamiliar? familiar);
        return (liveRank > 0 ? liveRank : familiar?.Rank ?? 0, CrucibleUi.ExpShare(familiar?.Exp ?? ""));
    }

    public static List<ReaderXBMPetParty.MonsterEntry> FightOrder(List<ReaderXBMPetParty.MonsterEntry> team)
    {
        var settings = BeastmasterRuntime.Settings;
        bool leveling = settings.TeamMode == CrucibleTeamMode.Leveling && settings.LevelingMode != CrucibleLevelingMode.Carry_Minus_2;
        bool clearTeam = BeastmasterSelection.RequiresClearTeam(settings);
        var candidates = team.Select(row => Candidate(row.Number) with
            { Rank = row.Rank > 0 ? row.Rank : Candidate(row.Number).Rank,
                Alive = !row.Disabled && (clearTeam ? row.MaxHP > 0 && row.HP > 0 && row.HP <= row.MaxHP : row.MaxHP == 0 || row.HP > 0) }).ToArray();
        var order = clearTeam
            ? BeastmasterSelection.ClearFightOrder(settings, candidates)
            : BeastmasterSelection.FightOrder(candidates, leveling);
        return order.Select(number => team.First(row => row.Number == number)).ToList();
    }

    public static List<ReaderXBMPetParty.MonsterEntry> RemainingFinalFightOrder(
        List<ReaderXBMPetParty.MonsterEntry> team, BeastmasterFightContext fight, IReadOnlyList<uint> planned)
    {
        var pets = Svc.Data.GetExcelSheet<XBMPet>();
        var candidates = team.Select(row => new BeastmasterCandidate(row.Number, row.Rank, 0, 0,
            Alive: !row.Disabled, HP: row.HP, MaxHP: row.MaxHP,
            AreaDamage: pets.TryGetRow(row.Number, out var sheet) && sheet.Pet.ValueNullable is { } pet &&
                pet.Abilities.Any(ability => ability.ValueNullable is { CanTargetHostile: true, CastType: > 1, EffectRange: > 0 }),
            AttackPower: Math.Max(row.Strength, row.Intelligence)));
        return BeastmasterSelection.RemainingFinalFightOrder(BeastmasterRuntime.Settings, fight, candidates, planned)
            .Select(number => team.First(row => row.Number == number)).ToList();
    }

    public static void UpdateCache()
    {
        DateTime now = DateTime.UtcNow;
        if (now < nextCacheRead)
            return;
        nextCacheRead = now + CacheInterval;

        if (!Player.Available || !BeastmasterRuntime.Service.CanObserveCharacter)
            return;

        bool changed = false;
        foreach (string window in DetailWindows)
            if (CrucibleUi.FamiliarDetail(window) is { } seen)
                changed |= RememberFamiliar(seen);

        try
        {
            if (!Scanning && CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* notebook) && CrucibleUi.BestiarySelected(notebook) is { } selected)
                changed |= RememberFamiliar(selected);
        }
        catch (BeastmasterReadUnavailableException) { /* Passive observation: preserve the last valid record. */ }

        try
        {
            if (CrucibleUi.Team() is { } rows)
                changed |= RememberTeam(rows);
        }
        catch (BeastmasterReadUnavailableException) { /* An incomplete screen must not interrupt the run. */ }

        if (changed)
            BeastmasterRuntime.Save();
    }

    public static bool RememberTeam(List<ReaderXBMPetParty.MonsterEntry> rows)
    {
        Svc.Log.Debug("Crucible Team - Remember Team");

        // Validate membership before recording any of this observation. A
        // partially populated roster is not an empty or changed successful team.
        if (rows.Any(row => row.Number == 0 || !FamiliarSheetDatas.ContainsKey(row.Number))) return false;
        bool       changed = false;
        List<uint> team    = new(rows.Count);
        foreach (ReaderXBMPetParty.MonsterEntry row in rows)
        {
            uint number = row.Number;
            if (number == 0)
                continue;

            team.Add(number);
            changed |= RememberFamiliarFromTeamRow(number, row);
        }

        BeastmasterCharacterData mine = Mine(true)!;
        if (!mine.Team.SequenceEqual(team))
        {
            mine.Team = team;
            changed   = true;
        }

        return changed;
    }

    public static List<uint> MissingRanks()
    {
        IReadOnlyDictionary<uint, CrucibleFamiliar> cached = Familiars;
        IReadOnlyList<uint>                         team   = CurrentTeam;
        return Owned().Where(x => !cached.TryGetValue(x, out CrucibleFamiliar? f) || f.Rank is < 1 or > 25 ||
            !team.Contains(x) && f.Exp.Length == 0).ToList();
    }

    private static DateTime scanningUntil;

    public static bool Scanning
    {
        get => DateTime.UtcNow < scanningUntil;
        set => scanningUntil = value ? DateTime.UtcNow.AddSeconds(2) : DateTime.MinValue;
    }

    public static void UpdateFamiliar(uint number, uint rank, uint xp)
    {
        if (rank is < 1 or > 25 || !FamiliarSheetDatas.ContainsKey(number) || !BeastmasterRuntime.Service.CanObserveCharacter) return;
        SortedDictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;
        if (familiars.TryGetValue(number, out CrucibleFamiliar? familiar))
        {
            familiar.Rank = (int)rank;
            familiar.Exp  = xp.ToString();
            familiar.ObservedAtUtc = DateTime.UtcNow;
            Mine(false)!.Owned.Add(number);
        }
    }

    public static bool RememberFamiliarUnsaved(CrucibleFamiliar seen) =>
        RememberFamiliar(seen);

    private static bool RememberFamiliar(CrucibleFamiliar seen)
    {
        if (seen.SheetData.Number == 0 || seen.Rank is < 1 or > 25 || !BeastmasterRuntime.Service.CanObserveCharacter)
            return false;

        SortedDictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;
        familiars.TryGetValue(seen.SheetData.Number, out CrucibleFamiliar? before);

        // A timestamp is evidence of a changed observation, not a reason to save
        // the same visible native row again on every cache update.
        seen.ObservedAtUtc = before?.ObservedAtUtc ?? DateTime.MinValue;
        if (seen != before || seen.ObservedAtUtc == DateTime.MinValue || !Mine(false)!.Owned.Contains(seen.Number))
        {
            if(before != null)
                if (seen.Rank < before.Rank)
                    return false;

            seen.ObservedAtUtc = DateTime.UtcNow;
            familiars[seen.SheetData.Number] = seen;
            Mine(false)!.Owned.Add(seen.Number);
            return true;
        }

        return false;
    }

    private static bool RememberFamiliarFromTeamRow(uint number, ReaderXBMPetParty.MonsterEntry row)
    {
        if (row.MaxHP == 0)
            return false;

        SortedDictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;

        CrucibleFamiliar familiar = familiars.TryGetValue(number, out var known) ? known : new CrucibleFamiliar(number);

        if (row.Rank < familiar.Rank || row.Rank is < 1 or > 25)
            return false;

        return RememberFamiliar(familiar with
        {
            Rank = row.Rank,
            // Team rows omit EXP. A new rank invalidates the old rank's EXP.
            Exp = row.Rank == familiar.Rank && familiar.ObservedAtUtc != DateTime.MinValue ? familiar.Exp : string.Empty,
            Hp = (int)row.MaxHP,
            Strength = row.Strength,
            PhysicalResistance = row.PhysResistance,
            Constitution = row.Constitution,
            Intelligence = row.Intelligence,
            MagicResistance = row.MagicResistance,
        });
    }
}

internal sealed unsafe class CrucibleTeamSetup
{
    private const int MaxActions = 40;

    private static readonly TimeSpan PreparationLimit = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan StepPatience = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Resend = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(250);

    private enum Step
    {
        Plan,
        AwaitMenu,
        AwaitRemoved,
        AwaitBestiary,
        AwaitNotebookClose,
        AddBatch,
        AwaitAdded,
        Settle
    }

    private CrucibleTeamMode mode;
    private List<uint>       goal = [];
    private Step             step;
    private DateTime         stepSince;
    private int              actions;
    private string           signature = "";
    private DateTime         signatureSince;
    private DateTime         requestedAt;
    private List<uint>       teamBeforeAction = [];
    private List<uint>       expectedAfterAction = [];

    private readonly List<uint> batch = [];
    private          int        batchTotal;

    private bool              selectionPrepared;
    private bool              notebookCloseSubmitted;

    private List<uint>? rankScanQueue;
    private readonly List<uint> rankScanUnread = [];
    private uint rankScanning;
    private DateTime rankScanStarted;
    private bool rankScanRetried;
    private bool rankScanChanged;
    private int rankScanTotal;
    private readonly HashSet<uint> rankScanPages = [];

    public System.Collections.Generic.IReadOnlyList<uint> RankScanUnread => this.rankScanUnread;
    public int RankScanReadCount => this.rankScanTotal - (this.rankScanQueue?.Count ?? 0) - this.rankScanUnread.Count;
    public int RankScanTotal => this.rankScanTotal;

    public void ResetRankScan()
    {
        if (this.rankScanChanged) BeastmasterRuntime.Save();
        this.rankScanQueue = null;
        this.rankScanUnread.Clear(); this.rankScanPages.Clear();
        this.rankScanning = 0; this.rankScanChanged = false;
        this.rankScanTotal = 0;
        this.requestedAt = DateTime.MinValue;
        CrucibleTeam.Scanning = false;
    }

    // Original first-run scan: Lauda's notebook, missing records only, immediate
    // acceptance of matching details. Never use PetParty as a notebook pointer.
    public bool Scan(AtkUnitBase* notebook, DateTime now)
    {
        if (!ResetFilter(notebook)) return false;
        CrucibleTeam.Scanning = true;
        var reader = new ReaderXBMMonsterNotebook(notebook);
        if (this.rankScanQueue == null)
        {
            var showing = reader.CurrentPageEntries.Select(entry => entry.Number).ToHashSet();
            this.rankScanQueue = CrucibleTeam.MissingRanks().OrderByDescending(showing.Contains).ThenBy(number => number).ToList();
            this.rankScanTotal = this.rankScanQueue.Count;
        }
        if (this.rankScanning != 0)
        {
            if (CrucibleUi.BestiarySelected(notebook) is { } selected && selected.Number == this.rankScanning)
            {
                this.rankScanChanged |= CrucibleTeam.RememberFamiliarUnsaved(selected);
                this.rankScanQueue.Remove(this.rankScanning);
                this.rankScanning = 0; this.rankScanPages.Clear();
            }
            else if (now - this.rankScanStarted > TimeSpan.FromMilliseconds(1500))
            {
                this.rankScanUnread.Add(this.rankScanning);
                this.rankScanQueue.Remove(this.rankScanning);
                this.rankScanning = 0; this.rankScanPages.Clear();
            }
            else if (!this.rankScanRetried && now - this.rankScanStarted > TimeSpan.FromMilliseconds(500))
            {
                this.rankScanRetried = true;
                Screens.Notebook.PickEntry(notebook, SlotOf(notebook, this.rankScanning));
            }
            return false;
        }
        if (this.rankScanQueue.Count == 0)
        {
            if (this.rankScanChanged) { BeastmasterRuntime.Save(); this.rankScanChanged = false; }
            CrucibleTeam.Scanning = false;
            return true;
        }
        var next = this.rankScanQueue[0];
        if (!CrucibleUi.BestiaryShows(notebook, next))
        {
            if (reader.PageCount == 0) return false;
            if (now - this.requestedAt < Resend) return false;
            if (!this.rankScanPages.Add(reader.CurrentPage))
            {
                this.rankScanUnread.Add(next); this.rankScanQueue.Remove(next); this.rankScanPages.Clear();
                return false;
            }
            Screens.Notebook.ShowPage(notebook, (reader.CurrentPage + 1) % reader.PageCount);
            this.requestedAt = now;
            return false;
        }
        this.Status = $"Reading familiar ranks ({this.rankScanTotal - this.rankScanQueue.Count + 1}/{this.rankScanTotal})";
        if (!Screens.Notebook.PickEntry(notebook, SlotOf(notebook, next)))
        {
            this.rankScanUnread.Add(next); this.rankScanQueue.Remove(next); this.rankScanPages.Clear();
            return false;
        }
        this.rankScanning = next; this.rankScanStarted = now; this.rankScanRetried = false;
        return false;
    }

    private const string UnconfirmedAdd = "A familiar was not confirmed in the team. Check the native team before restarting.";
    private const string UnexpectedTeam = "Team setup stopped because the team changed unexpectedly.";
    private const string UnconfirmedWindow = "Team setup stopped because a native window did not finish updating.";

    public string? Error  { get; private set; }
    public string  Status { get; private set; } = "";

    public void Start(CrucibleTeamMode teamMode)
    {
        this.mode = teamMode;
        this.goal = [];
        this.SetStep(Step.Plan, DateTime.UtcNow);
        this.actions        = 0;
        this.signature      = "";
        this.requestedAt    = DateTime.MinValue;
        this.batch.Clear();
        this.teamBeforeAction.Clear();
        this.expectedAfterAction.Clear();
        this.selectionPrepared = false;
        this.notebookCloseSubmitted = false;
        CrucibleTeam.Scanning = false;
        this.Error            = null;
        this.Status           = "Setting up the team";
    }

    private bool MatchesPlannedTeam(IReadOnlyCollection<uint> team) => this.selectionPrepared && this.Error == null &&
        BeastmasterSelection.CanEnterBoard(this.goal) &&
        BeastmasterSelection.TeamMatches(team, this.goal);

    public bool ValidateReadyTeam(out string reason)
    {
        if (!CrucibleUi.TryRegistrationTeam(out _, out var rows) ||
            !this.MatchesPlannedTeam(rows.Select(row => row.Number).ToList()))
        { reason = UnexpectedTeam; return false; }
        reason = string.Empty;
        return true;
    }

    public void ResumeAfterPause(TimeSpan elapsed)
    {
        Shift(ref this.stepSince); Shift(ref this.signatureSince); Shift(ref this.requestedAt);
        void Shift(ref DateTime value) { if (value != DateTime.MinValue) value += elapsed; }
    }

    public bool Update()
    {
        if (this.Error != null)
            return true;

        if (!EzThrottler.Throttle("CrucibleTeamRegistration", 100))
            return false;

        DateTime now = DateTime.UtcNow;

        if (!CrucibleUi.TryRegistrationTeam(out AtkUnitBase* party, out var rows))
            return (this.step is not (Step.Plan or Step.Settle)) && now - this.stepSince > StepPatience
                ? this.Fail(this.step == Step.AwaitAdded ? UnconfirmedAdd : UnconfirmedWindow) : false;

        List<uint> team = new(rows.Count);
        foreach (ReaderXBMPetParty.MonsterEntry row in rows)
        {
            uint number = row.Number;
            if (number == 0)
                return this.Fail($"Can't tell which familiar \"{row.Name}\" is. Open its details in the bestiary once so VERMAXION learns the name.");
            team.Add(number);
        }
        if (team.Distinct().Count() != team.Count)
            return this.Fail(UnexpectedTeam);

        if (!this.selectionPrepared)
        {
            if (BeastmasterRuntime.Settings.TeamMode != this.mode)
                return this.Fail(UnexpectedTeam);
            if (CrucibleTeam.RememberTeam(rows))
                BeastmasterRuntime.Save();
            if (!CrucibleTeam.TryPrepareSelection(out this.goal, out var reason))
                return this.Fail(reason);
            this.selectionPrepared = true;
            this.signature = "";
            this.SetStep(Step.Plan, now);
        }

        CrucibleTeam.UpdateCache();

        if (this.step == Step.AddBatch)
            return this.AddBatch(team, now);
        if (this.step == Step.AwaitAdded)
            return this.AwaitAdded(team, now);

        string read = string.Join(",", team);
        if (read != this.signature)
        {
            this.signature      = read;
            this.signatureSince = now;
            return false;
        }

        bool waitedTooLong = now - this.stepSince > StepPatience;

        Svc.Log.Debug("Crucible Team - Executing step: " + this.step);
        switch (this.step)
        {
            case Step.Plan:
                return this.Plan(party, team, now);

            case Step.AwaitMenu:
                if (!BeastmasterSelection.TeamMatches(team, this.teamBeforeAction))
                    return this.Fail(UnexpectedTeam);
                if (CrucibleUi.TryReady(CrucibleUi.ContextMenu, out AtkUnitBase* menu))
                {
                    Screens.Menu.ChooseFirst(menu);
                    this.SetStep(Step.AwaitRemoved, now);
                }
                else if (waitedTooLong)
                {
                    return this.Fail(UnconfirmedWindow);
                }

                return false;

            case Step.AwaitRemoved:
                if (BeastmasterSelection.TeamMatches(team, this.expectedAfterAction))
                {
                    if (CrucibleUi.IsVisible(CrucibleUi.ContextMenu) || CrucibleUi.IsVisible(CrucibleUi.YesNo))
                        return waitedTooLong ? this.Fail(UnconfirmedWindow) : false;
                    this.SetStep(Step.Plan, now);
                }
                else if (!BeastmasterSelection.TeamMatches(team, this.teamBeforeAction))
                    return this.Fail(UnexpectedTeam);
                else if (waitedTooLong)
                    return this.Fail(UnconfirmedWindow);
                return false;

            case Step.AwaitBestiary:
                if (CrucibleUi.IsOpen(CrucibleUi.BestiaryWindow))
                    this.SetStep(Step.Plan, now);
                else if (waitedTooLong)
                    return this.Fail(UnconfirmedWindow);
                return false;

            case Step.AwaitNotebookClose:
                if (!CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow))
                    this.SetStep(Step.Plan, now);
                else if (waitedTooLong)
                    return this.Fail(UnconfirmedWindow);
                else this.CloseNotebookIfReady();
                return false;

            case Step.Settle:
                if (this.goal.All(team.Contains) || now - this.signatureSince > SettleTime || waitedTooLong)
                    this.SetStep(Step.Plan, now);
                return false;
            case Step.AddBatch:
            case Step.AwaitAdded:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return false;
    }

    private static uint SlotOf(AtkUnitBase* notebook, uint number)
    {
        ReaderXBMMonsterNotebook x = new(notebook);

        for (int index = 0; index < x.CurrentPageEntries.Count; index++)
        {
            ReaderXBMMonsterNotebook.MonsterEntry entry = x.CurrentPageEntries[index];
            if (entry.Number == number)
                return (uint) index;
        }

        return uint.MaxValue;
    }

    private bool Plan(AtkUnitBase* party, List<uint> team, DateTime now)
    {
        if (!BeastmasterSelection.CanEnterBoard(this.goal))
            return this.Fail("Crucible entry requires at least three different familiars. Adjust the team selection and restrictions.");

        if (CrucibleUi.IsVisible(CrucibleUi.ContextMenu) || CrucibleUi.IsVisible(CrucibleUi.YesNo))
            return now - this.stepSince > StepPatience ? this.Fail(UnconfirmedWindow) : false;

        if (BeastmasterSelection.TeamMatches(team, this.goal))
        {
            if (this.CloseNotebookBeforePlanning(now))
                return false;
            this.Status = "Team ready";
            Svc.Log.Info($"[Crucible] Team ready: {Names(team)}");
            return true;
        }

        int extra = team.FindIndex(x => !this.goal.Contains(x));
        if (extra >= 0)
        {
            if (this.CloseNotebookBeforePlanning(now))
                return false;
            if (++this.actions > MaxActions)
                return this.Fail("Team setup kept going without settling; stopped.");
            this.teamBeforeAction = team.ToList();
            this.expectedAfterAction = team.Where((_, index) => index != extra).ToList();
            this.Status      = $"Removing {CrucibleTeam.NameOf(team[extra])}";
            Screens.PetParty.OpenRowMenu(party, extra);
            this.SetStep(Step.AwaitMenu, now);
            return false;
        }

        List<uint> missing = this.goal.Where(x => !team.Contains(x)).ToList();
        if (!CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* bestiary))
        {
            this.RequestBestiary(party, now);
            this.SetStep(Step.AwaitBestiary, now);
            return false;
        }

        if (!ResetFilter(bestiary))
            return now - this.stepSince > PreparationLimit ? this.Fail(UnconfirmedWindow) : false;

        if (++this.actions > MaxActions)
            return this.Fail("Team setup kept going without settling; stopped.");

        this.batch.Clear();

        IEnumerable<uint> showing = CrucibleUi.BestiaryShowing(bestiary);

        this.batch.AddRange(missing.OrderBy(x => showing.Contains(x) ? 0 : 1).ThenBy(x => x));
        this.batchTotal = this.batch.Count;
        this.SetStep(Step.AddBatch, now);
        return false;
    }

    private bool CloseNotebookBeforePlanning(DateTime now)
    {
        if (!CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow))
            return false;
        this.notebookCloseSubmitted = false;
        this.SetStep(Step.AwaitNotebookClose, now);
        this.CloseNotebookIfReady();
        return true;
    }

    private void CloseNotebookIfReady()
    {
        if (this.notebookCloseSubmitted || !CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* notebook)) return;
        this.notebookCloseSubmitted = true;
        Screens.Notebook.Close(notebook);
    }

    private bool AddBatch(List<uint> team, DateTime now)
    {
        if (this.batch.Count == 0)
        {
            this.signature = "";
            this.SetStep(Step.Settle, now);
            return false;
        }

        uint next = this.batch[0];
        if (team.Contains(next))
        {
            this.batch.RemoveAt(0);
            this.stepSince = now;
            return false;
        }
        if (team.Any(number => !this.goal.Contains(number)))
            return this.Fail(UnexpectedTeam);
        if (team.Count >= BeastmasterSelection.TeamCapacity(BeastmasterRuntime.SelectedTerritory))
            return this.Fail(UnconfirmedAdd);

        if (CrucibleUi.IsVisible(CrucibleUi.YesNo) || CrucibleUi.IsVisible(CrucibleUi.ContextMenu))
            return now - this.stepSince > StepPatience ? this.Fail(UnconfirmedWindow) : false;
        if (!CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* bestiary))
            return now - this.stepSince > StepPatience ? this.Fail(UnconfirmedWindow) : false;
        if (!this.ShowPageOf(bestiary, next, now))
        {
            if (now - this.stepSince > PreparationLimit)
                return this.Fail(UnconfirmedWindow);
            return false;
        }

        this.Status = $"Adding {CrucibleTeam.NameOf(next)} ({this.batchTotal - this.batch.Count + 1}/{this.batchTotal})";
        this.teamBeforeAction = team.ToList();
        if (!Screens.Notebook.PickEntry(bestiary, SlotOf(bestiary, next)))
            return this.Fail($"Couldn't pick No. {next} in the bestiary.");

        this.SetStep(Step.AwaitAdded, now);
        return false;
    }

    private bool AwaitAdded(List<uint> team, DateTime now)
    {
        var addition = BeastmasterSelection.ObserveAddition(this.teamBeforeAction, team, this.batch[0]);
        if (addition == BeastmasterTeamAddition.Confirmed)
        {
            this.batch.RemoveAt(0);
            this.SetStep(Step.AddBatch, now);
        }
        else if (addition == BeastmasterTeamAddition.Unexpected)
            return this.Fail(UnexpectedTeam);
        else if (now - this.stepSince > StepPatience)
            return this.Fail(UnconfirmedAdd);
        return false;
    }

    private bool ShowPageOf(AtkUnitBase* notebook, uint number, DateTime now)
    {
        if (CrucibleUi.BestiaryShows(notebook, number))
            return true;

        if (now - this.requestedAt > Resend)
        {
            Svc.Log.Debug($"Crucible Team - Switch to Page for {number}");
            ReaderXBMMonsterNotebook monsterNotebook = new(notebook);
            if (monsterNotebook.PageCount == 0)
                return false;

            Screens.Notebook.ShowPage(notebook, (monsterNotebook.CurrentPage + 1) % monsterNotebook.PageCount);
            this.requestedAt = now;
        }

        return false;
    }

    private void RequestBestiary(AtkUnitBase* party, DateTime now)
    {
        Screens.PetParty.OpenBestiary(party);
        this.requestedAt = now;
    }

    private static bool ResetFilter(AtkUnitBase* notebook)
    {
        ReaderXBMMonsterNotebook readerNotebook = new(notebook);
        if (!readerNotebook.Filtered)
            return true;
            
        Svc.Log.Debug("[Crucible] Bestiary Filter active");

        AtkUnitBase* bestiaryFilter = CrucibleUi.Ready(CrucibleUi.BestiaryWindowFilter);
        if (bestiaryFilter == null)
            Screens.Notebook.OpenFilter(notebook);
        else
            Screens.NotebookFilter.ResetFilters(bestiaryFilter);

        return false;
    }

    private void SetStep(Step next, DateTime now)
    {
        this.step        = next;
        this.stepSince   = now;
        this.requestedAt = DateTime.MinValue;
    }

    private bool Fail(string message)
    {
        this.Error  = message;
        this.Status = message;
        Svc.Log.Error($"[Crucible] {message} Step: {this.step}; pending additions: {this.batch.Count}.");
        return true;
    }

    private static string Names(IEnumerable<uint> team) =>
        string.Join(", ", team.Select(CrucibleTeam.NameOf));
}

