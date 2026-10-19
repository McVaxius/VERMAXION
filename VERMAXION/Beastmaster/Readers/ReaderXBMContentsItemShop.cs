// Adapted from ffxivcode/ECommons, commit 38fc2f7b2476e7c00e6383d4775701ccfd1183d8.
using Dalamud.Game.Text.SeStringHandling;
using ECommons.StringHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Beastmaster.Readers;

public unsafe class ReaderXBMContentsItemShop(AtkUnitBase* UnitBase, int BeginOffset = 0) : BeastmasterAtkReader(UnitBase, BeginOffset, "XBMContentsItemShop")
{

    public string CoinsString => ReadString(1);
    public int   Coins       => DigitParsers.FirstNumber(CoinsString.Replace(",", ""));


    public uint StockCount => ReadUInt(2) ?? 0u;


    private const int StockOffset    = 3;
    private const int StockEntrySize = 5;

    public List<StockEntry> StockEntries => ReadEntries<StockEntry>(StockOffset, StockEntrySize, StockCount, StockEntrySize, (154 - StockOffset) / StockEntrySize);

    public class StockEntry(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMContentsItemShop")
    {
        public readonly int purchaseIndex = (BeginOffset - StockOffset) / StockEntrySize;

        public bool Listed => ReadBool(0) ?? false;

        public uint     Item        => ReadUInt(1) ?? 0u;
        public SeString PriceString => ReadSeString(2);
        public int     Price       => DigitParsers.FirstNumber(PriceString.TextValue.Replace(",", ""));

        public bool Discounted => ReadBool(3) ?? false;
        public bool Bought     => ReadBool(4) ?? false;
    }

    public const int ITEM_ENTRY_SIZE   = 5;
    public const int ITEM_ENTRY_LENGTH = 10;

    public       List<ItemEntry>        ItemEntries      => ReadEntries<ItemEntry>(154, ITEM_ENTRY_SIZE, ITEM_ENTRY_LENGTH, ITEM_ENTRY_SIZE, allowNullTail: true);
    public       IEnumerable<ItemEntry> ItemEntriesValid => ItemEntries.Where(ie => ie.Id > 0);

    public class ItemEntry(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMContentsItemShop")
    {
        public int GetIndex(int initialOffset)
        {
            return (BeginOffset - initialOffset) / ITEM_ENTRY_SIZE;
        }

        public bool   Unk0     => ReadBool(0) ?? false;

        /// <summary>
        /// In Shops: Sellable. In Main: Usable
        /// </summary>
        public bool   Available => ReadBool(1) ?? false;
        public uint   IconId   => ReadUInt(2) ?? 0;
        public uint   Id       => ReadUInt(3) ?? 0;
        public string Name     => ReadString(4);
    }

    public const int GEAR_ENTRY_SIZE   = 5;
    public const int GEAR_ENTRY_LENGTH = 10;

    public List<GearEntry>        OwnedEntries      => ReadEntries<GearEntry>(205, GEAR_ENTRY_SIZE, GEAR_ENTRY_LENGTH, GEAR_ENTRY_SIZE, allowNullTail: true);
    public IEnumerable<GearEntry> OwnedEntriesOwned => OwnedEntries.Where(oe => oe.Owned);

    public class GearEntry(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMContentsItemShop")
    {
        public int GetIndex(int initialOffset)
        {
            return (BeginOffset - initialOffset) / GEAR_ENTRY_SIZE;
        }

        public bool   Owned  => ReadBool(0) ?? false;
        public uint   IconId => ReadUInt(2) ?? 0;
        public uint   Id     => ReadUInt(3) ?? 0;
        public string Name   => ReadString(4);
    }
}