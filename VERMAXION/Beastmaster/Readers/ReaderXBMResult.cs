// Adapted from ffxivcode/ECommons, commit 38fc2f7b2476e7c00e6383d4775701ccfd1183d8.
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;

namespace VERMAXION.Beastmaster.Readers;

public unsafe class ReaderXBMResult(AtkUnitBase* UnitBase, int BeginOffset = 0) : BeastmasterAtkReader(UnitBase, BeginOffset, "XBMResult")
{
    public uint Performance      => ReadUInt(2)  ?? 0;
    public uint MovesMadeScore   => ReadUInt(4)  ?? 0;
    public uint EnemyScore       => ReadUInt(6)  ?? 0;
    public uint EliteScore       => ReadUInt(8)  ?? 0;
    public uint BossScore        => ReadUInt(10) ?? 0;
    public uint RemainingHP      => ReadUInt(11) ?? 0;
    public uint RemainingHPScore => ReadUInt(12) ?? 0;

    public uint                  BonusBoostScore   => ReadUInt(13) ?? 0;
    public uint                  BonusBoostCount   => ReadUInt(14) ?? 0;
    // Names and scores occupy parallel arrays of sixteen slots.
    public List<BonusBoostEntry> BonusBoostEntries => ReadEntries<BonusBoostEntry>(15, 1, BonusBoostCount, 17, 16);
    public class BonusBoostEntry(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMResult")
    {
        public SeString Name  => ReadSeString(0);
        public uint     Score => ReadUInt(16) ?? 0;
    }

    public uint            LootCount   => ReadUInt(47) ?? 0;
    // Item, icon, name and quantity arrays each contain five slots.
    public List<LootEntry> LootEntries => ReadEntries<LootEntry>(48, 1, LootCount, 16, 5);
    public class LootEntry(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMResult")
    {
        public uint     ItemId => ReadUInt(0) ?? 0;
        public uint     IconId => ReadUInt(5) ?? 0;
        public SeString Name   => ReadSeString(10);
        public uint     Count  => ReadUInt(15) ?? 0;
    }

    public uint TotalScore => ReadUInt(68) ?? 0;
    public SeString Rank => ReadSeString(69);


    public uint BeastCount => ReadUInt(72) ?? 0;

    private const int              BeastOffset = 73;
    public        List<BeastEntry> BeastEntries => ReadEntries<BeastEntry>(BeastOffset, 1, BeastCount, 65, 16);

    public class BeastEntry(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMResult")
    {
        private const int Offset = 16;

        public int  IconId => ReadInt(0) ?? 0;
        public uint Number => (uint)(IconId - 242_000);

        public uint PrevXP => ReadUInt(1 * Offset) ?? 0;
        public uint NewXP  => ReadUInt(2 * Offset) ?? 0;

        public uint PrevRank => ReadUInt(3 * Offset) ?? 0;
        public uint NewRank  => ReadUInt(4 * Offset) ?? 0;
    }
}