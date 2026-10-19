// Adapted from ffxivcode/ECommons, commit 38fc2f7b2476e7c00e6383d4775701ccfd1183d8.
using Dalamud.Game.Text.SeStringHandling;
using ECommons.StringHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Beastmaster.Readers;

public unsafe class ReaderXBMMonsterNotebook(AtkUnitBase* UnitBase, int BeginOffset = 0) : BeastmasterAtkReader(UnitBase, BeginOffset, "XBMMonsterNotebook")
{
    public const int EntryCountPerPage = 25;

    public uint               PageCount          => ReadUInt(9)  ?? 0;
    public uint               CurrentPage        => ReadUInt(10) ?? 0;
    public List<MonsterEntry> CurrentPageEntries => ReadEntries<MonsterEntry>(24, 8, EntryCountPerPage, 1, allowNullTail: true);

    // Guarded typed reads distinguish unavailable data from a real zero EXP
    // value. CurrentPage remains zero-based; PageCount is a count,
    // matching the pinned reader and upstream ShowPage modulo calculation.
    internal BeastmasterNotebookPage ReadPage(uint nativePage)
    {
        var count = PageCount;
        var page = CurrentPage;
        var numbers = CurrentPageEntries.Select(entry => entry.Number).ToArray();
        return new(count > 0 && page < count && nativePage == page && numbers.Any(number => number != 0), count, page, numbers);
    }

    internal bool TryReadCurrentExperience(out uint experience)
        => TryReadUInt(261, out experience);

    public class MonsterEntry(nint UnitBasePtr, int BeginOffset = 0) : BeastmasterAtkReader(UnitBasePtr, BeginOffset, "XBMMonsterNotebook")
    {
        public uint     Number       => ReadUInt(0) ?? 0u;
        public bool     Unk1         => ReadBool(1) ?? false;
        public bool     Caught       => ReadBool(2) ?? false;
        public bool     Unk3         => ReadBool(3) ?? false;
        public uint     Unk4         => ReadUInt(4) ?? 0u;
        public SeString NumberString => ReadSeString(5);
        public bool     Unk6         => ReadBool(6) ?? false;
        public uint     Unk7         => ReadUInt(7) ?? 0;
    }

    public uint CurrentNumber  => ReadUInt(5) ?? 0;
    public uint SelectedNumber => ReadUInt(6) ?? 0;
    public bool Filtered => ReadBool(8) ?? false;

    public bool Selected => ReadBool(255) ?? false;

    public bool     CurrentRankCapped     => ReadBool(237) ?? false;
    public SeString CurrentName           => ReadSeString(230);
    public SeString CurrentClassification => ReadSeString(239);

    public SeString CurrentRankString => ReadSeString(258);
    public int      CurrentRank       => int.TryParse(CurrentRankString.TextValue.Trim(), out var rank) ? rank : 0;
    public uint     CurrentXP         => ReadUInt(261) ?? 0;
    public SeString CurrentHP => ReadSeString(259);
    public int CurrentMaxHP
    {
        get
        {
            var text = CurrentHP.TextValue;
            var ind  = text.IndexOf('/');

            return DigitParsers.Digits(text[(ind + 1)..]);
        }
    }
    public SeString CurrentStrengthString => ReadSeString(265);
    public int      CurrentStrength       => int.TryParse(CurrentStrengthString.TextValue.Trim(), out var i) ? i : 0;

    public SeString CurrentPhysResistanceString => ReadSeString(267);
    public int CurrentPhysResistance => int.TryParse(CurrentPhysResistanceString.TextValue.Trim(), out var i) ? i : 0;

    public SeString CurrentConstitutionString => ReadSeString(269);
    public int CurrentConstitution => int.TryParse(CurrentConstitutionString.TextValue.Trim(), out var i) ? i : 0;

    public SeString CurrentIntelligenceString => ReadSeString(271);
    public int CurrentIntelligence => int.TryParse(CurrentIntelligenceString.TextValue.Trim(), out var i) ? i : 0;

    public SeString CurrentMagicResistanceString => ReadSeString(273);
    public int CurrentMagicResistance => int.TryParse(CurrentMagicResistanceString.TextValue.Trim(), out var i) ? i : 0;
}
