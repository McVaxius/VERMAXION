// Adapted from ffxivcode/ECommons, commit 38fc2f7b2476e7c00e6383d4775701ccfd1183d8.
using ECommons.StringHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Beastmaster.Readers;

public unsafe class ReaderXBMContentsBooty(AtkUnitBase* UnitBase, int BeginOffset = 0) : BeastmasterAtkReader(UnitBase, BeginOffset, "XBMContentsBooty")
{
    public string CoinsString => ReadString(2);
    public int    Coins       => DigitParsers.FirstNumber(CoinsString.Replace(",", ""));

    public string LootCoinsString => ReadString(4);
    public int    LootCoins       => DigitParsers.FirstNumber(LootCoinsString.Replace(",", ""));
    public bool   LootCoinsTaken  => ReadBool(5) ?? false;

    private const int LootOffset = 9;
    private const int LootEntrySize = 5;
    public List<LootChoice> LootChoices => ReadLootChoices();

    private List<LootChoice> ReadLootChoices()
    {
        RequireSpan(129, 4);
        return ReadEntries<LootChoice>(LootOffset, LootEntrySize, 4, LootEntrySize, allowNullTail: true);
    }

    public class LootChoice(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMContentsBooty")
    {
        public readonly int lootIndex = (BeginOffset - LootOffset) / LootEntrySize;

        public uint Item => ReadUInt(0) ?? 0u;
        public bool Unk4 => ReadBool(2) ?? false;

        public bool Taken => ReadBool(129 + lootIndex - this.AtkReaderParams.BeginOffset) ?? false;
    }


    public List<ReaderXBMContentsItemShop.ItemEntry>        ItemEntries      => ReadEntries<ReaderXBMContentsItemShop.ItemEntry>(27, ReaderXBMContentsItemShop.ITEM_ENTRY_SIZE, ReaderXBMContentsItemShop.ITEM_ENTRY_LENGTH, ReaderXBMContentsItemShop.ITEM_ENTRY_SIZE, allowNullTail: true);
    public IEnumerable<ReaderXBMContentsItemShop.ItemEntry> ItemEntriesValid => ItemEntries.Where(ie => ie.Id > 0);

    public List<ReaderXBMContentsItemShop.GearEntry> OwnedEntries => ReadEntries<ReaderXBMContentsItemShop.GearEntry>(78, ReaderXBMContentsItemShop.GEAR_ENTRY_SIZE, ReaderXBMContentsItemShop.GEAR_ENTRY_LENGTH, ReaderXBMContentsItemShop.GEAR_ENTRY_SIZE, allowNullTail: true);
    public IEnumerable<ReaderXBMContentsItemShop.GearEntry> OwnedEntriesOwned => OwnedEntries.Where(oe => oe.Owned);
}