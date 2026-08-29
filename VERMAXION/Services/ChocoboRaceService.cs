using System;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Chocobo Racing queue service.
/// Uses the older ContentsFinder row-selection path for the normal fully-unlocked case.
/// Based on ChocoboRacing.lua: QueueRoulette(22) for Sagolii Road,
/// click ContentsFinderConfirm Commence, wait for race, wait for RaceChocoboResult.
/// This mirrors the VerminionService structure for consistency.
/// </summary>
public class ChocoboRaceService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly ChokeAboIpcClient chokeAboIpcClient;

    // Open the Gold Saucer duty pane through the same CFC anchor used in the older working path,
    // then select Chocobo Racing by row for characters that have the full set unlocked.
    private const uint ChocoboRaceAnchorCfcId = 576;
    private const int ChocoboRaceSelectionIndex = 10;
    private const byte MaxRaceChocoboRank = 50;

    private bool isActive = false;
    private ChocoboState state = ChocoboState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentAttempt = 0;
    private int maxAttempts = 5;
    private bool joinAttempted = false;
    private bool dutySelected = false;
    private int dutySelectionAttempts = 0;
    private DateTime lastJoinRetry = DateTime.MinValue;
    private uint returnHomeOriginTerritory;
    private string rankGateCheckReason = string.Empty;
    private bool targetCycleEnabledForBatch;
    private bool targetReadyForBatch;
    private DateTime nextTargetStatusPollAt = DateTime.MinValue;
    private string targetCycleStatusReason = string.Empty;

    public enum ChocoboState
    {
        Idle,
        WaitingForTargetCycle,
        CheckingRaceChocoboRank,
        OpeningGoldSaucerRankCheck,
        ReadingGoldSaucerRankCheck,
        ReturningHome,
        WaitingForHomeReady,
        OpeningDutyFinder,
        QueueingForDuty,
        WaitingForDutyPop,
        ClickingCommence,
        InDuty,
        WaitingForResult,
        DismissingResult,
        WaitingForPlayerAvailable,
        TestingGoldSaucerOpen,
        TestingGoldSaucerRank,
        Complete,
        Failed,
        Deferred,
    }

    public ChocoboState State => state;
    public int CurrentAttempt => currentAttempt;
    public bool IsActive => state != ChocoboState.Idle &&
                            state != ChocoboState.Complete &&
                            state != ChocoboState.Failed &&
                            state != ChocoboState.Deferred;
    public bool IsComplete => state == ChocoboState.Complete;
    public bool IsFailed => state == ChocoboState.Failed;
    public bool IsDeferred => state == ChocoboState.Deferred;
    public string StatusText => state switch
    {
        ChocoboState.Idle => "Idle",
        ChocoboState.WaitingForTargetCycle => $"Choke-abo target work: {targetCycleStatusReason}",
        ChocoboState.Deferred => $"Deferred: {targetCycleStatusReason}",
        ChocoboState.CheckingRaceChocoboRank => $"Checking rank before race {Math.Min(currentAttempt + 1, maxAttempts)}/{maxAttempts}",
        ChocoboState.OpeningGoldSaucerRankCheck => $"Opening GoldSaucerInfo rank fallback ({currentAttempt}/{maxAttempts})",
        ChocoboState.ReadingGoldSaucerRankCheck => $"Reading GoldSaucerInfo rank fallback ({currentAttempt}/{maxAttempts})",
        _ => $"{state} ({currentAttempt}/{maxAttempts})",
    };
    public string GoldSaucerRankTestStatus { get; private set; } = "Not tested yet.";

    public ChocoboRaceService(
        ICommandManager commandManager,
        IPluginLog log,
        ConfigManager configManager,
        ChokeAboIpcClient chokeAboIpcClient)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.condition = Plugin.Condition;
        this.configManager = configManager;
        this.chokeAboIpcClient = chokeAboIpcClient;
    }

    public bool Start()
    {
        // Get configured number of races from active character config
        var activeConfig = configManager?.GetActiveConfig();
        maxAttempts = activeConfig?.ChocoboRacesPerDay ?? 5;
        currentAttempt = 0;
        joinAttempted = false;
        dutySelected = false;
        dutySelectionAttempts = 0;
        lastJoinRetry = DateTime.MinValue;
        returnHomeOriginTerritory = 0;
        targetCycleEnabledForBatch = false;
        targetReadyForBatch = false;
        nextTargetStatusPollAt = DateTime.MinValue;
        targetCycleStatusReason = string.Empty;
        isActive = true;

        var mode = activeConfig?.ChocoboAutomationMode ?? ChocoboAutomationMode.AlwaysRace;
        if (!Enum.IsDefined(mode))
            return Defer($"Saved Chocobo automation mode value {(int)mode} is invalid.");

        if (mode == ChocoboAutomationMode.AlwaysRace)
        {
            if (chokeAboIpcClient.ShouldBlockRacing())
                return Defer("Choke-abo reports breeding with no race chocobo ready.");

            BeginNextRace();
            return true;
        }

        if (activeConfig == null)
            return Defer("No active character configuration is available for Target Pedigree mode.");

        targetCycleEnabledForBatch = true;
        return HandleTargetCycleResult(
            chokeAboIpcClient.EnsureTargetCycle(Plugin.PlayerState.ContentId, activeConfig));
    }

    private void BeginNextRace()
    {
        if (IsRankGateEnabled())
        {
            BeginRankGateCheckForNextRace();
            return;
        }

        log.Information("[ChocoboRace] Using VERMAXION observable one-race loop");
        ContinueToNextRaceQueue();
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Chocobo Racing triggered");
        if (!Start())
            Plugin.ChatGui.Print($"[Vermaxion] Chocobo Racing deferred: {targetCycleStatusReason}");
    }

    public void RequestGoldSaucerRankTest()
    {
        if (IsActive)
        {
            GoldSaucerRankTestStatus = $"Busy: {state}.";
            Plugin.ChatGui.Print($"[Vermaxion] Chocobo rank test busy: {state}.");
            return;
        }

        currentAttempt = 0;
        maxAttempts = 1;
        joinAttempted = false;
        dutySelected = false;
        dutySelectionAttempts = 0;
        GoldSaucerRankTestStatus = "Opening GoldSaucerInfo for rank test...";
        log.Information("[ChocoboRankTest] Opening GoldSaucerInfo with /goldsaucer");
        CommandHelper.SendCommand("/goldsaucer");
        SetState(ChocoboState.TestingGoldSaucerOpen);
    }

    public void Reset()
    {
        GameHelpers.KeyUp(VirtualKey.W);
        SetState(ChocoboState.Idle);
        isActive = false;
        stateEnteredAt = DateTime.MinValue;
        currentAttempt = 0;
        joinAttempted = false;
        dutySelected = false;
        dutySelectionAttempts = 0;
        lastJoinRetry = DateTime.MinValue;
        returnHomeOriginTerritory = 0;
        rankGateCheckReason = string.Empty;
        targetCycleEnabledForBatch = false;
        targetReadyForBatch = false;
        nextTargetStatusPollAt = DateTime.MinValue;
        targetCycleStatusReason = string.Empty;
    }

    public void Dispose() { }

    private bool IsRankGateEnabled()
    {
        var activeConfig = configManager.GetActiveConfig();
        return activeConfig?.SkipChocoboRacingAtRank50 == true;
    }

    private unsafe bool TryReadLoadedRaceChocoboManagerRank(out int currentRank)
    {
        currentRank = 0;
        try
        {
            var manager = RaceChocoboManager.Instance();
            if (manager == null || manager->State != RaceChocoboManager.RaceChocoboState.Loaded)
            {
                log.Debug("[ChocoboRace] RaceChocoboManager is not loaded; using GoldSaucerInfo rank fallback");
                return false;
            }

            currentRank = manager->Rank;
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[ChocoboRace] Failed to read RaceChocoboManager rank; using GoldSaucerInfo fallback: {ex.Message}");
            return false;
        }
    }

    private static bool TryParseRaceChocoboRank(string rawText, out int rank)
        => int.TryParse(rawText.Trim(), out rank);

    private void BeginRankGateCheckForNextRace()
    {
        rankGateCheckReason = currentAttempt == 0
            ? "before first race"
            : $"before race {currentAttempt + 1}/{maxAttempts}";
        log.Information($"[ChocoboRace] Checking racing chocobo rank {rankGateCheckReason}");
        SetState(ChocoboState.CheckingRaceChocoboRank);
    }

    private void StartFirstManualRace()
    {
        currentAttempt = 0;
        SetState(ChocoboState.ReturningHome);
        log.Information($"[ChocoboRace] Preparing Chocobo Racing cycle (0/{maxAttempts}) with /li home");
    }

    private void ContinueAfterRankGateAllowsRace(int rank, string source)
    {
        log.Information($"[ChocoboRace] Racing chocobo rank {rank} from {source}; continuing {rankGateCheckReason}");
        ContinueToNextRaceQueue();
    }

    private void ContinueToNextRaceQueue()
    {
        if (currentAttempt == 0)
        {
            StartFirstManualRace();
            return;
        }

        log.Information($"[ChocoboRace] Starting race {currentAttempt + 1}/{maxAttempts}");
        SetState(ChocoboState.OpeningDutyFinder);
    }

    private void CompleteBecauseRaceChocoboIsMaxRank(int rank, string source)
    {
        log.Information($"[ChocoboRace] Racing chocobo rank {rank} from {source}; completing daily racing task without queueing more races");
        SetState(ChocoboState.Complete);
    }

    private void FailRankGate(string reason)
    {
        GameHelpers.TryCloseAddonByCallback("GoldSaucerInfo");
        log.Warning($"[ChocoboRace] Could not read racing chocobo rank {rankGateCheckReason}: {reason}. Rank-50 skip is enabled, so the daily racing task is failing instead of racing blind.");
        SetState(ChocoboState.Failed);
    }

    private void EvaluateRankGateResult(int rank, string source)
    {
        GameHelpers.TryCloseAddonByCallback("GoldSaucerInfo");

        if (rank >= MaxRaceChocoboRank)
        {
            CompleteBecauseRaceChocoboIsMaxRank(rank, source);
            return;
        }

        ContinueAfterRankGateAllowsRace(rank, source);
    }

    /// <summary>
    /// Open the Duty Finder to a specific duty using AgentContentsFinder.
    /// The manual Chocobo fallback then selects the Chocobo Racing row from ContentsFinder.
    /// </summary>
    /// <param name="contentFinderConditionId">ContentFinderCondition row ID</param>
    public static unsafe bool OpenDutyFinder(uint contentFinderConditionId)
    {
        try
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                Plugin.Log.Error("[DutyQueue] AgentContentsFinder is null");
                return false;
            }
            agent->OpenRegularDuty(contentFinderConditionId);
            Plugin.Log.Information($"[DutyQueue] Opened duty finder for CFC ID {contentFinderConditionId}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DutyQueue] Failed to open duty finder: {ex.Message}");
            return false;
        }
    }

    public void Update()
    {
        if (state == ChocoboState.Idle ||
            state == ChocoboState.Complete ||
            state == ChocoboState.Failed ||
            state == ChocoboState.Deferred)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case ChocoboState.WaitingForTargetCycle:
                if (DateTime.UtcNow < nextTargetStatusPollAt)
                    return;

                HandleTargetCycleResult(
                    chokeAboIpcClient.GetTargetCycleStatus(Plugin.PlayerState.ContentId));
                return;

            case ChocoboState.CheckingRaceChocoboRank:
                if (elapsed < 0.1)
                    return;

                if (!IsRankGateEnabled())
                {
                    log.Information("[ChocoboRace] Rank-50 skip disabled while checking rank; continuing without rank gate");
                    ContinueToNextRaceQueue();
                    return;
                }

                if (TryReadLoadedRaceChocoboManagerRank(out var managerRank))
                {
                    EvaluateRankGateResult(managerRank, "RaceChocoboManager");
                    return;
                }

                log.Information("[ChocoboRace] Opening GoldSaucerInfo with /goldsaucer for rank fallback");
                CommandHelper.SendCommand("/goldsaucer");
                SetState(ChocoboState.OpeningGoldSaucerRankCheck);
                return;

            case ChocoboState.OpeningGoldSaucerRankCheck:
                if (elapsed < 0.5)
                    return;

                if (GameHelpers.IsAddonVisible("GoldSaucerInfo"))
                {
                    log.Information("[ChocoboRace] GoldSaucerInfo visible; reading node 21 for rank fallback");
                    SetState(ChocoboState.ReadingGoldSaucerRankCheck);
                    return;
                }

                if (elapsed > 10)
                {
                    FailRankGate("GoldSaucerInfo did not open within 10 seconds");
                    return;
                }
                return;

            case ChocoboState.ReadingGoldSaucerRankCheck:
                if (elapsed < 0.2)
                    return;

                if (!GameHelpers.TryGetAddonText("GoldSaucerInfo", 21u, out var rawRankText))
                {
                    FailRankGate("GoldSaucerInfo node 21 text was unavailable");
                    return;
                }

                if (!TryParseRaceChocoboRank(rawRankText, out var fallbackRank))
                {
                    FailRankGate($"GoldSaucerInfo node 21 text '{rawRankText}' was not numeric");
                    return;
                }

                log.Information($"[ChocoboRace] GoldSaucerInfo node 21 text='{rawRankText}', parsed rank={fallbackRank}");
                EvaluateRankGateResult(fallbackRank, "GoldSaucerInfo node 21");
                return;

            case ChocoboState.ReturningHome:
                if (elapsed < 0.5)
                    return;

                returnHomeOriginTerritory = Plugin.ClientState.TerritoryType;
                log.Information("[ChocoboRace] Returning home before opening ContentsFinder: /li home");
                commandManager.ProcessCommand("/li home");
                SetState(ChocoboState.WaitingForHomeReady);
                return;

            case ChocoboState.WaitingForHomeReady:
                if (elapsed < 3)
                    return;

                if (Plugin.ClientState.TerritoryType != returnHomeOriginTerritory && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[ChocoboRace] /li home completed, opening duty finder");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                else if (elapsed > 12 && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[ChocoboRace] /li home settled without a territory change, opening duty finder");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                else if (elapsed > 25)
                {
                    log.Warning("[ChocoboRace] Timed out waiting for /li home to settle, opening duty finder anyway");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                return;

            case ChocoboState.OpeningDutyFinder:
                if (elapsed < 1) return;
                log.Information($"[ChocoboRace] Starting Chocobo Racing queue (attempt {currentAttempt + 1}/{maxAttempts})");
                
                if (OpenDutyFinder(ChocoboRaceAnchorCfcId))
                {
                    joinAttempted = false;
                    dutySelected = false;
                    dutySelectionAttempts = 0;
                    lastJoinRetry = DateTime.MinValue;
                    SetState(ChocoboState.QueueingForDuty);
                }
                else
                {
                    log.Error("[ChocoboRace] Failed to open duty finder");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.QueueingForDuty:
                // Restore the older ContentsFinder-driven path for the normal unlocked case:
                // clear selection, pick the Chocobo Racing row, then press Join.
                if (elapsed < 6) return;
                
                if (GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    if (currentAttempt == 0 && elapsed < 6.5)
                    {
                        log.Information("[ChocoboRace] Clearing duty selection for first run");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 1);
                        return;
                    }

                    if (!dutySelected)
                    {
                        log.Information($"[ChocoboRace] Selecting Chocobo Racing row {ChocoboRaceSelectionIndex} from ContentsFinder");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 3, ChocoboRaceSelectionIndex);
                        dutySelectionAttempts++;
                        dutySelected = true;

                        log.Information("[ChocoboRace] Clicking Join after Chocobo selection");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        joinAttempted = true;
                        lastJoinRetry = DateTime.UtcNow;
                        return;
                    }

                    if (!joinAttempted && elapsed > 8)
                    {
                        log.Information("[ChocoboRace] Duty selected, clicking Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        joinAttempted = true;
                        lastJoinRetry = DateTime.UtcNow;
                    }
                    else if (joinAttempted && (DateTime.UtcNow - lastJoinRetry).TotalSeconds >= 5)
                    {
                        log.Information($"[ChocoboRace] ContentsFinder still visible after {elapsed:F1}s, retrying Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        lastJoinRetry = DateTime.UtcNow;
                    }
                }

                if (joinAttempted &&
                    (condition[ConditionFlag.WaitingForDutyFinder] || condition[ConditionFlag.WaitingForDuty]))
                {
                    log.Information("[ChocoboRace] Duty queue registered, waiting for race pop");
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                else if (joinAttempted && elapsed > 8 && !GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    log.Information("[ChocoboRace] ContentsFinder closed, waiting for duty pop");
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                else if (elapsed > 30)
                {
                    log.Warning($"[ChocoboRace] Timeout waiting for queue registration after selection attempts={dutySelectionAttempts}, retrying");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                break;

            case ChocoboState.WaitingForDutyPop:
                // Check for ContentsFinderConfirm addon (duty pop)
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Race pop! Clicking Commence");
                    SetState(ChocoboState.ClickingCommence);
                }
                // Also check if already in duty
                else if (condition[ConditionFlag.BoundByDuty])
                {
                    log.Information("[ChocoboRace] Already in duty");
                    SetState(ChocoboState.InDuty);
                }
                else if (elapsed > 120) // 2 min timeout
                {
                    log.Warning("[ChocoboRace] Duty queue timeout - retrying");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                break;

            case ChocoboState.ClickingCommence:
                if (elapsed < 1) return;
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Clicking Commence on ContentsFinderConfirm");
                    // Fire commence callback - typically callback index 8 = Commence button
                    GameHelpers.FireAddonCallback("ContentsFinderConfirm", true, 8);
                    SetState(ChocoboState.InDuty);
                }
                else
                {
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                break;

            case ChocoboState.InDuty:
                // Press W during the race and wait for RaceChocoboResult addon to appear
                if (GameHelpers.IsAddonVisible("RaceChocoboResult"))
                {
                    log.Information("[ChocoboRace] Race ended, RaceChocoboResult addon visible");
                    SetState(ChocoboState.WaitingForResult);
                }
                else if (!condition[ConditionFlag.BoundByDuty] && elapsed > 10)
                {
                    // Duty ended without result screen
                    log.Information("[ChocoboRace] Duty ended");
                    SetState(ChocoboState.WaitingForPlayerAvailable);
                }
                else if (elapsed > 600) // 10 min timeout
                {
                    log.Warning("[ChocoboRace] Race timeout");
                    SetState(ChocoboState.Failed);
                }
                else if (elapsed > 5 && elapsed < 7) // Hold W key after 5 seconds
                {
                    GameHelpers.KeyDown(VirtualKey.W);
                }
                break;

            case ChocoboState.WaitingForResult:
                if (elapsed < 1) return;
                // Release W key when race ends
                GameHelpers.KeyUp(VirtualKey.W);
                
                // Check both possible addon names and log which one we're using
                bool raceResult = GameHelpers.IsAddonVisible("RaceChocoboResult");
                bool chocoboResult = GameHelpers.IsAddonVisible("ChocoboResult");
                
                if (raceResult)
                {
                    log.Information("[ChocoboRace] Dismissing RaceChocoboResult screen");
                    GameHelpers.FireAddonCallback("RaceChocoboResult", true, 1);
                    SetState(ChocoboState.DismissingResult);
                }
                else if (chocoboResult)
                {
                    log.Information("[ChocoboRace] Dismissing ChocoboResult screen");
                    GameHelpers.FireAddonCallback("ChocoboResult", true, 1);
                    SetState(ChocoboState.DismissingResult);
                }
                else
                {
                    SetState(ChocoboState.WaitingForPlayerAvailable);
                }
                break;

            case ChocoboState.DismissingResult:
                if (elapsed < 1) return;
                bool raceResultCheck = GameHelpers.IsAddonVisible("RaceChocoboResult");
                bool chocoboResultCheck = GameHelpers.IsAddonVisible("ChocoboResult");
                
                if (raceResultCheck)
                {
                    // Try again to dismiss RaceChocoboResult
                    GameHelpers.FireAddonCallback("RaceChocoboResult", true, 1);
                }
                else if (chocoboResultCheck)
                {
                    // Try again to dismiss ChocoboResult
                    GameHelpers.FireAddonCallback("ChocoboResult", true, 1);
                }
                SetState(ChocoboState.WaitingForPlayerAvailable);
                break;

            case ChocoboState.WaitingForPlayerAvailable:
                // Wait until player is available for next race
                if (elapsed < 2) return;
                if (GameHelpers.IsPlayerAvailable())
                {
                    currentAttempt++;
                    log.Information($"[ChocoboRace] Race {currentAttempt}/{maxAttempts} complete");

                    if (targetCycleEnabledForBatch && !targetReadyForBatch)
                    {
                        var activeConfig = configManager.GetActiveConfig();
                        if (activeConfig == null || activeConfig.ChocoboAutomationMode != ChocoboAutomationMode.TargetPedigree)
                        {
                            Defer("Target Pedigree mode changed before the post-race Choke-abo handoff.");
                            return;
                        }

                        HandleTargetCycleResult(
                            chokeAboIpcClient.EnsureTargetCycle(Plugin.PlayerState.ContentId, activeConfig));
                    }
                    else if (currentAttempt >= maxAttempts)
                    {
                        log.Information($"[ChocoboRace] All {maxAttempts} races complete!");
                        SetState(ChocoboState.Complete);
                    }
                    else
                    {
                        BeginNextRace();
                    }
                }
                else if (elapsed > 30)
                {
                    log.Error("[ChocoboRace] Timed out waiting for verified player availability after race");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.TestingGoldSaucerOpen:
                if (elapsed < 0.5)
                    return;

                if (GameHelpers.IsAddonVisible("GoldSaucerInfo"))
                {
                    log.Information("[ChocoboRankTest] GoldSaucerInfo visible; reading node 21");
                    SetState(ChocoboState.TestingGoldSaucerRank);
                    return;
                }

                if (elapsed > 10)
                {
                    GoldSaucerRankTestStatus = "Failed: GoldSaucerInfo did not open.";
                    log.Warning("[ChocoboRankTest] GoldSaucerInfo did not open within 10 seconds");
                    Plugin.ChatGui.Print("[Vermaxion] Chocobo rank test failed: GoldSaucerInfo did not open.");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.TestingGoldSaucerRank:
                if (elapsed < 0.2)
                    return;

                if (!GameHelpers.TryGetAddonText("GoldSaucerInfo", 21u, out var rawText))
                {
                    GoldSaucerRankTestStatus = "Failed: GoldSaucerInfo node 21 unavailable.";
                    log.Warning("[ChocoboRankTest] GoldSaucerInfo node 21 text was unavailable");
                    Plugin.ChatGui.Print("[Vermaxion] Chocobo rank test failed: GoldSaucerInfo node 21 unavailable.");
                    SetState(ChocoboState.Failed);
                    return;
                }

                if (!TryParseRaceChocoboRank(rawText, out var rank))
                {
                    GoldSaucerRankTestStatus = $"Failed: could not parse node 21 text '{rawText}'.";
                    log.Warning($"[ChocoboRankTest] Could not parse GoldSaucerInfo node 21 text '{rawText}'");
                    Plugin.ChatGui.Print($"[Vermaxion] Chocobo rank test failed: node 21 text '{rawText}' was not a number.");
                    SetState(ChocoboState.Failed);
                    return;
                }

                var maxRank = rank >= MaxRaceChocoboRank;
                GoldSaucerRankTestStatus = $"GoldSaucerInfo node 21 rank={rank}. Rank 50 skip={(maxRank ? "YES" : "NO")}.";
                log.Information($"[ChocoboRankTest] GoldSaucerInfo node 21 text='{rawText}', parsed rank={rank}, rank50={maxRank}");
                Plugin.ChatGui.Print($"[Vermaxion] Chocobo rank test: node 21 rank={rank}; rank 50 skip={(maxRank ? "YES" : "NO")}.");
                SetState(ChocoboState.Complete);
                break;
        }
    }

    private void SetState(ChocoboState newState)
    {
        log.Information($"[ChocoboRace] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        isActive = newState != ChocoboState.Idle &&
                   newState != ChocoboState.Complete &&
                   newState != ChocoboState.Failed &&
                   newState != ChocoboState.Deferred;
    }

    private bool HandleTargetCycleResult(ChokeAboTargetCycleCallResult result)
    {
        var decision = ChocoboTargetCyclePolicy.DecideHandoff(result, currentAttempt, maxAttempts);
        targetCycleStatusReason = string.IsNullOrWhiteSpace(decision.Reason)
            ? "Choke-abo returned no target-cycle reason."
            : decision.Reason;

        switch (decision.Action)
        {
            case ChocoboTargetHandoffAction.Wait:
                if (state != ChocoboState.WaitingForTargetCycle)
                    SetState(ChocoboState.WaitingForTargetCycle);
                nextTargetStatusPollAt = DateTime.UtcNow.AddSeconds(1);
                return true;

            case ChocoboTargetHandoffAction.Race:
                targetReadyForBatch |= decision.TargetReady;
                log.Information($"[ChocoboRace] Choke-abo yielded racing: {targetCycleStatusReason}");
                BeginNextRace();
                return true;

            case ChocoboTargetHandoffAction.Complete:
                log.Information($"[ChocoboRace] Daily race batch completed after Choke-abo handoff: {targetCycleStatusReason}");
                SetState(ChocoboState.Complete);
                return true;

            default:
                return Defer(targetCycleStatusReason);
        }
    }

    private bool Defer(string reason)
    {
        targetCycleStatusReason = reason;
        log.Information($"[ChocoboRace] Deferred: {reason}");
        SetState(ChocoboState.Deferred);
        return false;
    }
}
