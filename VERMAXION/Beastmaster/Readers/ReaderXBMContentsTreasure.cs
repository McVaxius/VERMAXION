// Adapted from ffxivcode/ECommons, commit 38fc2f7b2476e7c00e6383d4775701ccfd1183d8.
using Dalamud.Game.Text.SeStringHandling;
using ECommons.StringHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Beastmaster.Readers;

public unsafe class ReaderXBMContentsTreasure(AtkUnitBase* UnitBase, int BeginOffset = 0) : BeastmasterAtkReader(UnitBase, BeginOffset, "XBMContentsTreasure")
{
    public string CoinsString => ReadString(2);
    public int    Coins       => DigitParsers.FirstNumber(CoinsString.Replace(",", ""));

    private const int TreasureOffset = 3;
    private const int TreasureEntrySize = 5;

    public List<TreasureChoice> TreasureChoices => ReadTreasureChoices();

    private List<TreasureChoice> ReadTreasureChoices()
    {
        RequireSpan(126, 4);
        return ReadEntries<TreasureChoice>(TreasureOffset, TreasureEntrySize, 4, TreasureEntrySize, allowNullTail: true);
    }

    public class TreasureChoice(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMContentsTreasure")
    {
        public readonly int treasureIndex = (BeginOffset - TreasureOffset) / TreasureEntrySize;

        public bool Unk0 => ReadBool(0) ?? false;
        public uint Item => ReadUInt(3) ?? 0u;
        public bool Bought => ReadBool(126 + treasureIndex - this.AtkReaderParams.BeginOffset) ?? false;
    }


    public List<ReaderXBMContentsItemShop.ItemEntry>        ItemEntries      => ReadEntries<ReaderXBMContentsItemShop.ItemEntry>(24, ReaderXBMContentsItemShop.ITEM_ENTRY_SIZE, ReaderXBMContentsItemShop.ITEM_ENTRY_LENGTH, ReaderXBMContentsItemShop.ITEM_ENTRY_SIZE, allowNullTail: true);
    public IEnumerable<ReaderXBMContentsItemShop.ItemEntry> ItemEntriesValid => ItemEntries.Where(ie => ie.Id > 0);


    public List<ReaderXBMContentsItemShop.GearEntry> OwnedEntries => ReadEntries<ReaderXBMContentsItemShop.GearEntry>(75, ReaderXBMContentsItemShop.GEAR_ENTRY_SIZE, ReaderXBMContentsItemShop.GEAR_ENTRY_LENGTH, ReaderXBMContentsItemShop.GEAR_ENTRY_SIZE, allowNullTail: true);
    public IEnumerable<ReaderXBMContentsItemShop.GearEntry> OwnedEntriesOwned => OwnedEntries.Where(oe => oe.Owned);
}