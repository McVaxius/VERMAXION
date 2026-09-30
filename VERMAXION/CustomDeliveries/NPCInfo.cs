// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for VERMAXION.
using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.DeliverySupport;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace VERMAXION.CustomDeliveries;

public record class NPCInfo
{
    public int Index;
    public uint RowId;
    public uint TurninId;
    public string Name;
    public int MaxDeliveries;
    public readonly int[] SupplyIndices;
    public bool Unlocked;
    public int Rank;
    public int SatisfactionCur;
    public int SatisfactionMax;
    public int UsedDeliveries;
    public uint[] Requests = [];
    public bool[] IsBonusOverride = [false, false, false];
    public bool[] IsBonusEffective = [false, false, false];
    public uint[] EffectiveRequests = [0, 0, 0]; // accounts for bonus override
    public uint[] Rewards = [0, 0, 0];
    public uint[] TurnInItems = [0, 0, 0];
    public uint AchievementId;
    public uint? AchievementAtSample;
    public int UsedDeliveriesAtSample;
    public uint AchievementMax;
    public uint TerritoryId; // where turn-in npc is located (note: we assume crafting vendor is always is the same zone)
    public CraftTurnin? CraftData;
    public FishData? FishData;
    public GatherData? GatherData;
    public ushort[] MinCollectibility = [0, 0, 0];
    public ushort[] TargetCollectibility = [0, 0, 0];
    public int[] RequiredLevels = [0, 0, 0];

    public NPCInfo(SatisfactionNpc Row)
    {
        RowId = Row.RowId;
        Index = checked((int)(Row.RowId - 1));
        TurninId = Row.Npc.RowId;
        Name = Row.Npc.Value.Singular.ToString();
        MaxDeliveries = Row.DeliveriesPerWeek;
        SupplyIndices = [.. Row.SatisfactionNpcParams.Select(p => p.SupplyIndex)];
        TerritoryId = Row.Level.Value.Territory.RowId;
        AchievementId = VERMAXION.DeliverySupport.SatisfactionNpcSupport.GetAchievementId(Row);
        CraftData = new((uint)SupplyIndices[1], TurninId, TerritoryId);
    }

    public uint SupplyIndex => Rank >= 0 && Rank < SupplyIndices.Length ? (uint)SupplyIndices[Rank] : 0;
    public uint? AchievementCur => AchievementAtSample is { } current
        ? Math.Min(current + (uint)Math.Max(0, UsedDeliveries - UsedDeliveriesAtSample), AchievementMax)
        : null;
    public bool IsUnlocked => Service.LuminaRow<SatisfactionNpc>(RowId) is { QuestRequired.RowId: var questId }
        && questId != 0 && QuestManager.IsQuestComplete(questId);

    public int RemainingTurnins(int requestIndex)
    {
        var res = Math.Max(0, MaxDeliveries - UsedDeliveries);
        if (requestIndex is < 0 or > 2 || TurnInItems[requestIndex] == 0)
            return 0;
        if (SatisfactionMax > SatisfactionCur)
        {
            var reward = Service.LuminaRow<SatisfactionSupplyReward>(Rewards[requestIndex])?.SatisfactionHigh ?? 0;
            if (reward > 0)
                res = Math.Min(res, (int)Math.Ceiling((SatisfactionMax - SatisfactionCur) / (float)reward));
        }
        return res;
    }

    internal void ClearAchievement()
    {
        AchievementAtSample = null;
        AchievementMax = 0;
        UsedDeliveriesAtSample = 0;
    }
}
