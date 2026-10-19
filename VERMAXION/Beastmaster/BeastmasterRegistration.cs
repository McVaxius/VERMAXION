// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f.
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Memory;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Screens = VERMAXION.Beastmaster.CrucibleUi.Screens;
namespace VERMAXION.Beastmaster;

internal sealed unsafe partial class BeastmasterService
{
    public const uint LaudaTerritory = 148;
    private static readonly uint[] LaudaDataIds = [1059316, 1059339, 1059759];
    private static readonly Vector3 LaudaPosition = new(25.50f, -6f, 67.52f);
    private readonly CrucibleTeamSetup teamSetup = new();
    private int boardStep;
    private bool fastRankScanPending;
    private bool fastRankScanOpened;
    private bool fastRankScanObserved;

    private void RequestFastRankScan()
    {
        teamSetup.ResetRankScan();
        fastRankScanPending = true;
        fastRankScanOpened = fastRankScanObserved = false;
    }

    private void CancelFastRankScan()
    {
        fastRankScanPending = fastRankScanOpened = fastRankScanObserved = false;
        teamSetup.ResetRankScan();
    }

    private static bool HasOpenRegistrationScreen => Svc.ClientState.TerritoryType == LaudaTerritory &&
        (CrucibleUi.IsOpen(CrucibleUi.BoardList) ||
            CrucibleUi.IsOpen(CrucibleUi.BoardLayout) && CrucibleUi.IsOpen(CrucibleUi.TeamWindow) ||
            CrucibleUi.IsOpen(CrucibleUi.BestiaryWindow) ||
            Svc.Targets.Target is { } target && LaudaDataIds.Contains(target.BaseId) &&
                (CrucibleUi.IsOpen("SelectString") || CrucibleUi.IsOpen("SelectIconString") || CrucibleUi.IsOpen("Talk")));

    private static bool RegistrationInteractionReady => RegistrationPlayerReady &&
        (!Svc.Condition[ConditionFlag.OccupiedInQuestEvent] || HasOpenRegistrationScreen);

    private static bool RegistrationPlayerReady => Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded &&
        Svc.PlayerState.ContentId != 0 && BeastmasterPlayer.IsValid &&
        Svc.Objects.LocalPlayer is { IsDead: false, CurrentHp: > 0, IsCasting: false } &&
        !Svc.Condition[ConditionFlag.BoundByDuty] && !Svc.Condition[ConditionFlag.InCombat] &&
        !Svc.Condition[ConditionFlag.Casting] && !Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] &&
        !Svc.Condition[ConditionFlag.WatchingCutscene] && !Svc.Condition[ConditionFlag.WatchingCutscene78];

    private bool TryCloseTrainingRegistration()
    {
        if (!confirmingTrainingCompletion || operatorStopped || IsPaused ||
            Svc.ClientState.TerritoryType != LaudaTerritory || !CanObserveCharacter || !RegistrationPlayerReady ||
            !NativeQueueIsIdle() || CrucibleUi.IsVisible(CrucibleUi.YesNo) || CrucibleUi.IsVisible(CrucibleUi.ContextMenu))
            return false;

        if (CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow))
        {
            if (EzThrottler.Throttle("CrucibleOpenBoard", 250) && CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out var notebook))
                Screens.Notebook.Close(notebook);
            return false;
        }
        var window = CrucibleUi.IsVisible(CrucibleUi.TeamWindow) ? CrucibleUi.TeamWindow
            : CrucibleUi.IsVisible(CrucibleUi.BoardLayout) ? CrucibleUi.BoardLayout
            : CrucibleUi.IsVisible(CrucibleUi.BoardList) ? CrucibleUi.BoardList
            : CrucibleUi.IsVisible("SelectString") ? "SelectString"
            : CrucibleUi.IsVisible("SelectIconString") ? "SelectIconString"
            : CrucibleUi.IsVisible("Talk") ? "Talk" : null;
        if (window == null) return RegistrationInteractionReady;
        if (EzThrottler.Throttle("CrucibleOpenBoard", 250) && CrucibleUi.TryReady(window, out var addon))
            addon->Close(true);
        return false;
    }

        private static string ChallengeText =>
            field ??= Svc.Data.GetExcelSheet<RawRow>(name: "custom/009/CtsXbmEntrance_00976").TryGetRow(1, out RawRow row)
                                  ? row.ReadStringColumn(1).ExtractText().TrimEnd('.', '。', ' ')
                                  : string.Empty;
        private static string BestiaryText =>
            field ??= Svc.Data.GetExcelSheet<RawRow>(name: "custom/009/CtsXbmEntrance_00976").TryGetRow(3, out RawRow row)
                                  ? row.ReadStringColumn(1).ExtractText().TrimEnd('.', ' ', '\u3002')
                                  : string.Empty;
        private static string QuestAlternativeText =>
            field ??= Svc.Data.GetExcelSheet<CustomTalk>().TryGetRow(721872, out CustomTalk row)
                                  ? row.MainOption.ExtractText().TrimEnd('.', '。', ' ')
                                  : string.Empty;

        private unsafe void ChooseSelectStringText(AtkUnitBase* menu)
        {
            string text = this.fastRankScanPending ? BestiaryText : ChallengeText;
            Svc.Log.Debug("[Crucible] Select String " + text);

            int index = text.Length > 0 ? CrucibleUi.SelectStringIndex(menu, text) : -1;
            if (index < 0)
            {
                Svc.Log.Warning($"[Crucible] Lauda's menu has no \"{text}\" option");
                return;
            }

            BeastmasterAddon.FireCallBack(menu, true, index);
            if (this.fastRankScanPending) this.fastRankScanOpened = true;
        }

        private unsafe bool OpenBoard(uint board)
        {
            if (this.teamSetup.Error is { } setupError) { this.Fail(setupError); return false; }
            void DebugLog(string message) => 
                Svc.Log.Debug($"Crucible Open Board: {message}");

            if (this.fastRankScanPending)
            {
                var registration = CrucibleUi.IsVisible(CrucibleUi.TeamWindow) ? CrucibleUi.TeamWindow
                    : CrucibleUi.IsVisible(CrucibleUi.BoardLayout) ? CrucibleUi.BoardLayout
                    : CrucibleUi.IsVisible(CrucibleUi.BoardList) ? CrucibleUi.BoardList : null;
                if (registration != null)
                {
                    if (EzThrottler.Throttle("CrucibleOpenBoard", 250) && CrucibleUi.TryReady(registration, out var previous))
                        previous->Close(true);
                    return false;
                }
                if (CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out var scanningNotebook))
                {
                    if (!this.fastRankScanOpened)
                    {
                        if (EzThrottler.Throttle("CrucibleOpenBoard", 250)) Screens.Notebook.Close(scanningNotebook);
                        return false;
                    }
                    if (!this.fastRankScanObserved)
                    {
                        if (!this.PrepareRunOwnership(out _)) return false;
                        this.fastRankScanObserved = true;
                    }
                    if (!this.teamSetup.Scan(scanningNotebook, DateTime.UtcNow))
                    { this.status = this.teamSetup.Status; return false; }
                    this.fastRankScanPending = false;
                    this.refreshStatus = this.teamSetup.RankScanUnread.Count == 0
                        ? $"Refreshed ranks and EXP for {this.teamSetup.RankScanReadCount} owned beasts."
                        : $"Beast refresh incomplete: {this.teamSetup.RankScanReadCount}/{this.teamSetup.RankScanReadCount + this.teamSetup.RankScanUnread.Count} observed; unread: {string.Join(", ", this.teamSetup.RankScanUnread.Select(number => $"{CrucibleTeam.NameOf(number)} (#{number})"))}. The bestiary did not show the requested beast's rank and EXP; partial observations were retained.";
                    Screens.Notebook.Close(scanningNotebook);
                    if (this.Settings.Goal == BeastmasterRunGoal.TrainOwned && this.TrainingRanksMeetTarget())
                        this.confirmingTrainingCompletion = true;
                    return false;
                }
                if (CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow)) return false;
            }

            if (CrucibleUi.IsOpen(CrucibleUi.TeamWindow))
            {
                DebugLog("Done");
                return true;
            }

            if (!EzThrottler.Throttle("CrucibleOpenBoard", 250))
                return false;

            if (CrucibleUi.TryReady(CrucibleUi.BoardList, out AtkUnitBase* list))
            {
                DebugLog("Board List available");

                if (this.boardStep == 0)
                {
                    DebugLog("Board List Highlight");
                    Screens.StageList.Highlight(list, board);
                    this.boardStep = 1;
                }
                else
                {
                    DebugLog("Board List Select");
                    Screens.StageList.Open(list, board);
                    this.boardStep = 0;
                    EzThrottler.Throttle("CrucibleOpenBoard", 250, true);
                }

                return false;
            }

            if (CrucibleUi.TryReady("SelectIconString", out AtkUnitBase* iconMenu))
            {
                DebugLog("Pre board Select");

                AddonSelectIconString * select = (AddonSelectIconString*) iconMenu;
                ref PopupMenu           iconPopMenu = ref select->PopupMenu.PopupMenu;
                if (iconPopMenu.EntryNames == null)
                    return false;

                for (int i = 0; i < iconPopMenu.EntryCount; i++)
                {
                    if (iconPopMenu.EntryNames[i].Value == null)
                        continue;

                    string entry = MemoryHelper.ReadSeStringNullTerminated((nint)iconPopMenu.EntryNames[i].Value).TextValue;

                    DebugLog($"Pre board Select. Entry: {entry} | {QuestAlternativeText} | {entry.Contains(QuestAlternativeText, StringComparison.OrdinalIgnoreCase)}");

                    if (QuestAlternativeText.Length > 0 && entry.Contains(QuestAlternativeText, StringComparison.OrdinalIgnoreCase))
                    {
                        BeastmasterAddon.FireCallBack(iconMenu, true, i);
                        break;
                    }
                }

                EzThrottler.Throttle("CrucibleOpenBoard", 250, true);
                return false;
            }

            if (CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* notebook))
            {
                DebugLog("Closing the bestiary before registration");
                Screens.Notebook.Close(notebook);
                return false;
            }
            if (CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow)) return false;

            if (CrucibleUi.TryReady("SelectString", out AtkUnitBase* menu))
            {
                DebugLog("Main Select");
                ChooseSelectStringText(menu);
                EzThrottler.Throttle("CrucibleOpenBoard", 250, true);
                return false;
            }

            if (GenericHelpers.TryGetAddonByName("Talk", out AtkUnitBase* talk) && GenericHelpers.IsAddonReady(talk))
            {
                BeastmasterAddon.ClickTalk();
                return false;
            }

            if (!BeastmasterPlayer.IsReady)
                return false;

            if (Svc.Objects.FirstOrDefault(o => o.IsTargetable && LaudaDataIds.Contains(o.BaseId)) is not { } lauda)
                return false;

            VERMAXION.Services.GameHelpers.InteractWithObject(lauda);
            EzThrottler.Throttle("CrucibleOpenBoard", 200, true);
            return false;
        }


}
