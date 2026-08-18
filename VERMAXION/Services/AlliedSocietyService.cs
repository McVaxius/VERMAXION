using System;
using System.Linq;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class AlliedSocietyService
{
    private static readonly TimeSpan GearsetTimeout = TimeSpan.FromSeconds(15);

    private readonly IEquipmentAutomationRuntime equipment;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly QuestionableCompanionAlliedSocietyBridge bridge;
    private readonly GearsetConfirmationWindow confirmationWindow = new();
    private GearsetSnapshot? selectedGearset;
    private DateTime stateEnteredAt;
    private ulong startingContentId;
    private bool equipIssued;

    public enum RunState
    {
        Idle,
        EquippingGearset,
        ConfirmingGearset,
        WaitingForGearset,
        StartingRotation,
        MonitoringRotation,
        Complete,
        Failed,
        Cancelled,
    }

    public AlliedSocietyService(
        IEquipmentAutomationRuntime equipment,
        IClientState clientState,
        IPlayerState playerState,
        IObjectTable objectTable,
        IPluginLog log,
        QuestionableCompanionAlliedSocietyBridge bridge)
    {
        this.equipment = equipment;
        this.clientState = clientState;
        this.playerState = playerState;
        this.objectTable = objectTable;
        this.log = log;
        this.bridge = bridge;
    }

    public RunState State { get; private set; } = RunState.Idle;
    public bool IsActive => State is not (RunState.Idle or RunState.Complete or RunState.Failed or RunState.Cancelled);
    public bool IsComplete => State == RunState.Complete;
    public bool IsFailed => State is RunState.Failed or RunState.Cancelled;
    public bool OwnsRotation => bridge.OwnsRun;
    public string StatusText { get; private set; } = "Idle";

    public void Start(CharacterConfig config)
    {
        Reset();
        if (bridge.OwnsRun)
        {
            Fail("A previous owned Allied Society run could not be stopped.");
            return;
        }
        if (!clientState.IsLoggedIn || !playerState.IsLoaded || equipment.CharacterContentId == 0)
        {
            Fail("Current character data is unavailable.");
            return;
        }

        var gearsets = equipment.GetValidGearsets();
        selectedGearset = config.AlliedSocietyGearsetSelection switch
        {
            AlliedSocietyGearsetSelection.CurrentJob => EquipmentAutomationPolicy.SelectCurrentGearset(
                gearsets,
                equipment.CurrentGearsetId,
                equipment.CurrentJobId),
            AlliedSocietyGearsetSelection.SavedGearset => gearsets.FirstOrDefault(
                gearset => gearset.GearsetId == config.AlliedSocietyGearsetId),
            _ => null,
        };
        if (selectedGearset == null)
        {
            Fail(config.AlliedSocietyGearsetSelection == AlliedSocietyGearsetSelection.SavedGearset
                ? "The selected Allied Society gearset is not a valid saved gearset."
                : "Current Job requires a valid active saved gearset.");
            return;
        }

        startingContentId = equipment.CharacterContentId;
        SetState(RunState.EquippingGearset, $"Equipping Allied Society gearset {selectedGearset.GearsetId}.");
    }

    public void Update()
    {
        if (!IsActive || selectedGearset == null)
            return;

        if (equipment.CharacterContentId != startingContentId)
        {
            Cancel("Character changed during the owned Allied Society run.");
            return;
        }

        switch (State)
        {
            case RunState.EquippingGearset:
                if (equipment.IsGearsetEquipped(selectedGearset.GearsetId, selectedGearset.ClassJobId))
                {
                    SetState(RunState.StartingRotation, "Selected gearset verified; resolving Questionable Companion.");
                    break;
                }

                if (equipIssued)
                {
                    SetState(RunState.WaitingForGearset, "Waiting for the selected Allied Society gearset.");
                    break;
                }

                equipIssued = true;
                if (!equipment.TryEquipGearset(selectedGearset.GearsetId, out var equipError))
                    StatusText = $"Native gearset request returned an error; checking its confirmation prompt: {equipError}";
                confirmationWindow.Open(equipment.UtcNow);
                SetState(RunState.ConfirmingGearset, StatusText);
                break;

            case RunState.ConfirmingGearset:
                if (confirmationWindow.Poll(equipment))
                    SetState(RunState.WaitingForGearset, "Verifying selected Allied Society gearset.");
                break;

            case RunState.WaitingForGearset:
                if (equipment.IsGearsetEquipped(selectedGearset.GearsetId, selectedGearset.ClassJobId))
                    SetState(RunState.StartingRotation, "Selected gearset verified; resolving Questionable Companion.");
                else if (equipment.UtcNow - stateEnteredAt >= GearsetTimeout)
                    Fail("Selected Allied Society gearset did not become active within 15 seconds.");
                break;

            case RunState.StartingRotation:
                if (!TryGetCurrentCharacterKey(out var characterKey, out var characterError))
                {
                    Fail(characterError);
                    break;
                }
                if (!equipment.IsGearsetEquipped(selectedGearset.GearsetId, selectedGearset.ClassJobId))
                {
                    Fail("Selected Allied Society gearset changed before StartRotation.");
                    break;
                }
                if (!bridge.TryStart(characterKey, out var startError))
                {
                    Fail($"Questionable Companion Allied Society start failed: {startError}");
                    break;
                }

                SetState(RunState.MonitoringRotation, $"Allied Society rotation active for {characterKey}.");
                break;

            case RunState.MonitoringRotation:
                if (!bridge.TryReadOwnedState(out var active, out var phase, out var pollError))
                {
                    Fail($"Owned Allied Society state became unreadable: {pollError}");
                    break;
                }

                if (active)
                {
                    StatusText = $"Questionable Companion Allied Society phase: {phase}.";
                    break;
                }

                bridge.ReleaseOwnership();
                SetState(RunState.Complete, "Current-character Allied Society rotation completed.");
                break;
        }
    }

    public void Cancel(string reason = "Allied Society task cancelled")
    {
        if (!IsActive && !bridge.OwnsRun)
            return;

        if (bridge.OwnsRun && !bridge.TryStopOwned(out var stopError))
            log.Warning($"[AlliedSociety] Owned StopRotation failed during cancellation: {stopError}");
        SetState(RunState.Cancelled, reason);
    }

    public void Reset()
    {
        if (bridge.OwnsRun)
            bridge.TryStopOwned(out _);
        selectedGearset = null;
        stateEnteredAt = DateTime.MinValue;
        startingContentId = 0;
        equipIssued = false;
        confirmationWindow.Reset();
        State = RunState.Idle;
        StatusText = "Idle";
    }

    private bool TryGetCurrentCharacterKey(out string key, out string error)
    {
        key = string.Empty;
        var player = objectTable.LocalPlayer;
        if (!clientState.IsLoggedIn || !playerState.IsLoaded || playerState.ContentId != startingContentId ||
            player == null)
        {
            error = "Current Name@HomeWorld identity is unavailable or changed.";
            return false;
        }

        try
        {
            var playerName = player.Name.ToString();
            var homeWorld = playerState.HomeWorld.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(homeWorld))
            {
                error = "Current Name@HomeWorld identity is unavailable or changed.";
                return false;
            }

            key = $"{playerName}@{homeWorld}";
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Current Name@HomeWorld identity could not be read: {ex.Message}";
            return false;
        }
    }

    private void Fail(string error)
    {
        if (bridge.OwnsRun && !bridge.TryStopOwned(out var stopError))
            error = $"{error} Owned StopRotation failed: {stopError}";
        SetState(RunState.Failed, error);
        log.Warning($"[AlliedSociety] {error}");
    }

    private void SetState(RunState state, string status)
    {
        State = state;
        StatusText = status;
        stateEnteredAt = equipment.UtcNow;
    }
}
