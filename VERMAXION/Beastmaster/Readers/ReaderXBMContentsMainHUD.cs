// Adapted from ffxivcode/ECommons, commit 38fc2f7b2476e7c00e6383d4775701ccfd1183d8.
using ECommons.StringHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Beastmaster.Readers;

public unsafe class ReaderXBMContentsMainHUD(AtkUnitBase* UnitBase, int BeginOffset = 0) : BeastmasterAtkReader(UnitBase, BeginOffset, "XBMContentsMainHUD")
{
    public string CoinsString => ReadString(8);
    public int    Coins       => DigitParsers.FirstNumber(CoinsString.Replace(",", ""));

    private const int ItemOffset = 9;
    public int GetItemIndex(ReaderXBMContentsItemShop.ItemEntry item)
    {
        return item.GetIndex(ItemOffset);
    }
    public List<ReaderXBMContentsItemShop.ItemEntry>        ItemEntries      => ReadEntries<ReaderXBMContentsItemShop.ItemEntry>(ItemOffset, ReaderXBMContentsItemShop.ITEM_ENTRY_SIZE, ReaderXBMContentsItemShop.ITEM_ENTRY_LENGTH, ReaderXBMContentsItemShop.ITEM_ENTRY_SIZE, allowNullTail: true);
    public IEnumerable<ReaderXBMContentsItemShop.ItemEntry> ItemEntriesValid => ItemEntries.Where(ie => ie.Id > 0);


    public List<ReaderXBMContentsItemShop.GearEntry>        OwnedEntries      => ReadEntries<ReaderXBMContentsItemShop.GearEntry>(60, ReaderXBMContentsItemShop.GEAR_ENTRY_SIZE, ReaderXBMContentsItemShop.GEAR_ENTRY_LENGTH, ReaderXBMContentsItemShop.GEAR_ENTRY_SIZE, allowNullTail: true);
    public IEnumerable<ReaderXBMContentsItemShop.GearEntry> OwnedEntriesOwned => OwnedEntries.Where(oe => oe.Owned);
}