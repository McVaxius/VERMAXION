using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using VERMAXION.IPC;

namespace VERMAXION.Services;

/// <summary>Coordinates the existing Verminion task and its bounded control proof.</summary>
public sealed class VerminionService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly LifestreamIPC lifestream;
    private readonly VNavmeshIPC navigation;
    private DateTime stateEnteredAt;
    private ulong owner;
    private bool navigationRequested;
    private bool ownsTravel;
    private DateTime nextInteractionUtc;
    private bool admissionConfirmed;
    private bool admissionSnapshot;
    private int tutorialPromptFloor;
    private string handledPrompt = string.Empty;
    private DateTime promptHandledUtc;
    private DateTime nextPromptCheckUtc;
    private VirtualKey? heldKey;
    private DateTime releaseKeyUtc;
    private bool pendingGateSummon;
    private int pendingTutorialSummons;
    private bool pendingTutorialMove;
    private string reason = "Idle";

    public enum VerminionState
    {
        Idle, TravellingToSaucer, TravellingToMinionSquare, ApproachingTable,
        InspectingControls, SelectingTutorial, WaitingForTutorial, InDuty, Complete, Failed,
    }

    public VerminionState State { get; private set; }
    public int CurrentAttempt => 0;
    public bool IsActive => State is not (VerminionState.Idle or VerminionState.Complete or VerminionState.Failed);
    public bool IsComplete => State == VerminionState.Complete;
    public bool IsFailed => State == VerminionState.Failed;
    public string StatusText => reason;

    public VerminionService(ICommandManager commandManager, ICondition condition, IPluginLog log,
        ConfigManager configManager, LifestreamIPC lifestream, VNavmeshIPC navigation)
    {
        this.commandManager = commandManager;
        this.condition = condition;
        this.log = log;
        this.configManager = configManager;
        this.lifestream = lifestream;
        this.navigation = navigation;
    }

    public void Start()
    {
        if (IsActive) return;
        if (configManager.GetActiveConfig().VerminionPaused)
        { Fail("Paused by FULL STOP; use Run/Resume."); return; }
        owner = Plugin.PlayerState.ContentId;
        if (owner == 0) { Fail("No current character."); return; }
        VerminionGameInteraction.CaptureSetup();
        VerminionGameInteraction.CaptureBattle("start observation");
        admissionConfirmed = false;
        admissionSnapshot = false;
        tutorialPromptFloor = VerminionGameInteraction.CurrentLogIndex();
        handledPrompt = string.Empty;
        pendingGateSummon = false;
        pendingTutorialSummons = 0;
        pendingTutorialMove = false;
        nextPromptCheckUtc = DateTime.MinValue;
        if (VerminionGameInteraction.HasTutorialQueue())
        {
            admissionConfirmed = true;
            SetState(VerminionState.WaitingForTutorial, "Resuming the verified Stage 1 queue");
            return;
        }
        if (VerminionGameInteraction.IsAdmissionPrompt)
        { SetState(VerminionState.WaitingForTutorial, "Resuming tutorial admission"); return; }
        if (VerminionGameInteraction.IsChallengeMenu)
        { SetState(VerminionState.SelectingTutorial, "Resuming tutorial selection"); return; }
        if (VerminionGameInteraction.IsSetupMenu)
        { SetState(VerminionState.InspectingControls, "Resuming the verified Verminion setup menu"); return; }
        if (condition[ConditionFlag.PlayingLordOfVerminion])
        {
            SetState(VerminionState.InDuty, "Resuming tutorial control proof");
            return;
        }
        if (!GameHelpers.IsPlayerAvailable() || condition[ConditionFlag.BoundByDuty] ||
            condition[ConditionFlag.InDutyQueue] || condition[ConditionFlag.WaitingForDutyFinder] ||
            !lifestream.TryReadBusy(out var busy) || busy)
        { Fail("Character, duty queue, or travel is busy/unavailable."); return; }
        if (!VerminionGameInteraction.PrepareOwnedPalette())
        { Fail("Three owned minions could not be verified on the palette. No purchases made."); return; }
        if (Plugin.ClientState.TerritoryType == 388)
        { SetState(VerminionState.ApproachingTable, "Approaching the Verminion table"); return; }
        if (Plugin.ClientState.TerritoryType == 144)
        { TravelToMinionSquare(); return; }
        ownsTravel = lifestream.ExecuteCommand("/li saucer");
        if (!ownsTravel) { Fail("Gold Saucer travel was rejected."); return; }
        SetState(VerminionState.TravellingToSaucer, "Travelling to the Gold Saucer");
    }

    public void RunTask()
    {
        if (IsActive) return;
        configManager.GetActiveConfig().VerminionPaused = false;
        configManager.SaveCurrentAccount();
        Start();
    }

    public void Reset()
    {
        StopOwnedMovement();
        owner = 0;
        navigationRequested = false;
        SetState(VerminionState.Idle, "Idle");
    }

    public void Dispose() => Reset();

    // Shared with other Gold Saucer tasks; opening is not selection or admission.
    public static unsafe bool OpenDutyFinder(uint contentFinderConditionId)
    {
        var agent = AgentContentsFinder.Instance();
        if (agent == null) return false;
        agent->OpenRegularDuty(contentFinderConditionId);
        return true;
    }

    public void Update()
    {
        if (!IsActive) return;
        if (owner == 0 || Plugin.PlayerState.ContentId != owner)
        { Fail("Character changed; no result recorded."); return; }
        if (configManager.GetActiveConfig().VerminionPaused)
        { Fail("Paused; no result recorded."); return; }
        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;
        try
        {
            switch (State)
            {
                case VerminionState.TravellingToSaucer:
                    if (elapsed > 120) { Fail("Gold Saucer travel timed out."); return; }
                    if (elapsed < 3 || !TravelSettled() || Plugin.ClientState.TerritoryType != 144) return;
                    ownsTravel = false;
                    TravelToMinionSquare();
                    return;
                case VerminionState.TravellingToMinionSquare:
                    if (elapsed > 90) { Fail("Minion Square travel timed out."); return; }
                    if (elapsed < 3 || !TravelSettled() || Plugin.ClientState.TerritoryType != 388) return;
                    ownsTravel = false;
                    SetState(VerminionState.ApproachingTable, "Approaching the Verminion table");
                    return;
                case VerminionState.ApproachingTable:
                    if (elapsed > 60) { Fail("Could not reach the Verminion table."); return; }
                    var player = Plugin.ObjectTable.LocalPlayer;
                    if (player == null || !GameHelpers.IsPlayerAvailable()) return;
                    var table = Plugin.ObjectTable.Where(obj => obj.BaseId == 2006529 && obj.IsTargetable)
                        .OrderBy(obj => Vector3.DistanceSquared(player.Position, obj.Position)).FirstOrDefault();
                    if (table == null) { Fail("No Verminion table is loaded."); return; }
                    if (Vector3.Distance(player.Position, table.Position) > 3.8f)
                    {
                        var direction = Vector3.Normalize(new Vector3(player.Position.X - table.Position.X, 0, player.Position.Z - table.Position.Z));
                        var approach = table.Position + direction * 2.4f;
                        approach.Y = player.Position.Y;
                        if (!navigationRequested) navigationRequested = navigation.PathfindAndMoveTo(approach);
                        return;
                    }
                    if (navigationRequested) { navigation.Stop(); navigationRequested = false; }
                    if (DateTime.UtcNow < nextInteractionUtc) return;
                    nextInteractionUtc = DateTime.UtcNow.AddSeconds(2);
                    if (!VerminionGameInteraction.InteractWithTable(table)) return;
                    SetState(VerminionState.InspectingControls, "Reading the Verminion setup menu");
                    return;
                case VerminionState.InspectingControls:
                    if (elapsed < 3) return;
                    if (GameHelpers.TrySelectStringExact("Verminion Challenge", out _))
                        SetState(VerminionState.SelectingTutorial, "Selecting the tutorial");
                    else if (elapsed > 10) Fail("The verified Verminion Challenge menu is unavailable.");
                    return;
                case VerminionState.SelectingTutorial:
                    if (elapsed < 2) return;
                    VerminionGameInteraction.CaptureAddon("SelectString");
                    VerminionGameInteraction.CaptureAddon("SelectIconString");
                    if (GameHelpers.TrySelectStringExact("Stage 1: Tutorial", out _))
                        SetState(VerminionState.WaitingForTutorial, "Waiting for tutorial admission");
                    else Fail("Challenge menu captured; the tutorial entry requires verification.");
                    return;
                case VerminionState.WaitingForTutorial:
                    if (elapsed > 60) { VerminionGameInteraction.CaptureBattle("admission timeout"); Fail("Tutorial admission timed out; no result recorded."); return; }
                    if (elapsed < 2) return;
                    if (!admissionConfirmed && VerminionGameInteraction.IsAdmissionPrompt)
                        admissionConfirmed = GameHelpers.TryClickNativeButton("SelectYesno", "Yes", 8);
                    if (admissionConfirmed && DateTime.UtcNow >= nextInteractionUtc && VerminionGameInteraction.TryCommenceTutorial())
                        nextInteractionUtc = DateTime.UtcNow.AddSeconds(5);
                    if (elapsed > 10 && !admissionSnapshot)
                    {
                        admissionSnapshot = true;
                        VerminionGameInteraction.CaptureBattle("ten seconds after admission selection");
                    }
                    if (condition[ConditionFlag.PlayingLordOfVerminion])
                        SetState(VerminionState.InDuty, "Observing the tutorial battlefield");
                    return;
                case VerminionState.InDuty:
                    if (elapsed < 3) return;
                    if (!VerminionGameInteraction.IsTutorialBattle())
                    { Fail("The current battle is not the verified Stage 1 tutorial."); return; }
                    UpdateTutorialProof();
                    return;
            }
        }
        catch (Exception ex) { Fail($"Interaction failed: {ex.Message}. No result recorded."); }
    }

    private bool TravelSettled() => GameHelpers.IsPlayerAvailable() && lifestream.TryReadBusy(out var busy) && !busy;

    private void TravelToMinionSquare()
    {
        ownsTravel = lifestream.AethernetTeleportById(89);
        if (!ownsTravel) { Fail("Minion Square aethernet travel was rejected."); return; }
        SetState(VerminionState.TravellingToMinionSquare, "Travelling to Minion Square");
    }

    private void StopOwnedMovement()
    {
        ReleaseKey();
        if (owner != 0 && owner == Plugin.PlayerState.ContentId)
        {
            if (navigationRequested) navigation.Stop();
            if (ownsTravel) commandManager.ProcessCommand("/li stop");
        }
        navigationRequested = false;
        ownsTravel = false;
    }

    private void ReleaseKey()
    {
        if (heldKey is not { } key) return;
        heldKey = null;
        GameHelpers.KeyUp(key);
    }

    private unsafe bool TryMoveCamera()
    {
        var input = UIInputData.Instance();
        var binding = input == null ? null : input->GetKeybind(InputId.MOVE_FORE);
        if (binding == null) return false;
        foreach (var setting in binding->KeySettings)
        {
            if ((byte)setting.Key == 0 || (byte)setting.KeyModifier != 0) continue;
            heldKey = (VirtualKey)setting.Key;
            releaseKeyUtc = DateTime.UtcNow.AddSeconds(1);
            GameHelpers.KeyDown(heldKey.Value);
            log.Information("[VerminionControl] camera input dispatched using Move Forward binding; awaiting tutorial readback");
            return true;
        }
        return false;
    }

    private void UpdateTutorialProof()
    {
        var now = DateTime.UtcNow;
        if (heldKey != null && now >= releaseKeyUtc) ReleaseKey();
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
        { ReleaseKey(); return; }
        if (now < nextPromptCheckUtc) return;
        nextPromptCheckUtc = now.AddSeconds(1);
        if (pendingTutorialMove)
        {
            pendingTutorialMove = false;
            VerminionGameInteraction.CaptureBattle("after hatchling selection, before movement");
            var marker = Plugin.ObjectTable.FirstOrDefault(obj => obj.BaseId == 2005110);
            if (marker == null || !VerminionGameInteraction.TryClickBattlefield(marker.Position, rightClick: true))
                Fail("Tutorial destination marker or movement control unavailable; no result recorded.");
            return;
        }
        if (pendingGateSummon)
        {
            pendingGateSummon = false;
            VerminionGameInteraction.CaptureAddon("LovmPalette");
            if (!VerminionGameInteraction.TrySummonPaletteSlot(0))
                Fail("Gate selection sent but summon unavailable; no result recorded.");
            return;
        }
        if (pendingTutorialSummons > 0)
        {
            if (!VerminionGameInteraction.TrySummonPaletteSlot(0))
            { Fail("Tutorial summon queue is unavailable; no result recorded."); return; }
            --pendingTutorialSummons;
            return;
        }
        var prompt = VerminionGameInteraction.ReadTutorialPrompt(tutorialPromptFloor);
        if (string.IsNullOrEmpty(prompt) || prompt == handledPrompt)
        {
            if ((now - (handledPrompt.Length == 0 ? stateEnteredAt : promptHandledUtc)).TotalSeconds > 60)
            {
                VerminionGameInteraction.CaptureBattle("tutorial instruction/readback timeout");
                Fail("No new tutorial instruction after 60 seconds; no result recorded.");
            }
            return;
        }
        ReleaseKey();
        log.Information($"[VerminionControl] fresh tutorial instruction: {prompt}");
        var dispatched = prompt switch
        {
            "Use the movement keys to shift your viewpoint around." => TryMoveCamera(),
            "Select a minion from the minion hotbar." =>
                GameHelpers.TryGetAddonText("LovmPalette", 67, out var capacity) && capacity == "0/60" &&
                VerminionGameInteraction.TrySummonPaletteSlot(0),
            "Left-click on the “A” found on the display above the hotbar, and try summoning a minion from Gate A." =>
                pendingGateSummon = VerminionGameInteraction.TryClickPaletteIcon(73),
            "Summon several minions, and view them in the summoning queue." =>
                (pendingTutorialSummons = 3) > 0,
            "Select the hatchling, and move it inside the yellow circle. Left-click to select a minion, and right-click to select the destination." =>
                TrySelectTutorialHatchling(),
            _ => false,
        };
        if (!dispatched)
        {
            VerminionGameInteraction.CaptureBattle("unhandled tutorial instruction");
            Fail($"Control proof stopped at tutorial instruction: {prompt}");
            return;
        }
        handledPrompt = prompt;
        promptHandledUtc = now;
        reason = $"Tutorial: {prompt}";
    }

    private bool TrySelectTutorialHatchling()
    {
        var hatchling = Plugin.ObjectTable.FirstOrDefault(obj => obj.Name.TextValue == "Wayward Hatchling");
        return pendingTutorialMove = hatchling != null &&
            VerminionGameInteraction.TryClickBattlefield(hatchling.Position + new Vector3(0, 0.1f, 0), rightClick: false);
    }

    private void Fail(string message)
    {
        StopOwnedMovement();
        SetState(VerminionState.Failed, message);
    }

    private void SetState(VerminionState state, string message)
    {
        log.Information($"[Verminion] {State} -> {state}: {message}");
        State = state;
        reason = message;
        stateEnteredAt = DateTime.UtcNow;
    }
}
