using System;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VERMAXION.Beastmaster.Readers;

// Keep native shape failures separate from unrelated reader or automation errors.
public abstract unsafe class BeastmasterAtkReader : AtkReader
{
    private readonly AtkUnitBase* unit;
    private readonly int beginOffset;
    private readonly string window;

    protected BeastmasterAtkReader(AtkUnitBase* unit, int beginOffset, string window)
        : base(unit, beginOffset)
    {
        this.unit = unit;
        this.beginOffset = beginOffset;
        this.window = window;
    }

    protected BeastmasterAtkReader(nint unit, int beginOffset, string window)
        : this((AtkUnitBase*)unit, beginOffset, window) { }

    private BeastmasterReadUnavailableException Unavailable(string detail)
    {
        var name = unit == null ? window : unit->NameString;
        return new(string.IsNullOrEmpty(name) ? window : name, detail);
    }

    protected void RequireSpan(int offset, uint count = 1, int stride = 1, int width = 1)
    {
        if (unit == null || unit->AtkValues == null ||
            !BeastmasterReadBounds.Contains(unit->AtkValuesCount, offset, count, stride, width))
            throw Unavailable($"Native values do not contain offset {offset}, count {count}, stride {stride}, width {width}.");
    }

    // As in ECommons, list offsets are absolute, including for nested readers.
    // Advertised counts must be complete; fixed capacities retain a null tail.
    protected List<T> ReadEntries<T>(int offset, int stride, uint count, int width, uint? capacity = null, bool allowNullTail = false)
        where T : BeastmasterAtkReader
    {
        if (capacity is { } limit && count > limit)
            throw Unavailable($"Native row count {count} exceeds the {limit} slots at offset {offset}.");
        if (count > int.MaxValue)
            throw Unavailable($"Native row count {count} is unsupported.");
        // A fixed-capacity list can end with an observed Undefined sentinel
        // before its maximum capacity. Never require unused rows beyond it.
        if (!allowNullTail || count == 0) RequireSpan(offset, count, stride, width);
        var entries = new List<T>((int)count);
        for (var i = 0; i < (int)count; i++)
        {
            var absolute = (long)offset + (long)i * stride;
            if (absolute < 0 || absolute > int.MaxValue)
                throw Unavailable("Native row offset is outside the supported range.");
            var entryOffset = (int)absolute;
            RequireSpan(entryOffset);
            if (unit->AtkValues[entryOffset].Type == AtkValueType.Undefined)
            {
                if (allowNullTail) break;
                throw Unavailable($"Native row at offset {entryOffset} is unavailable.");
            }
            RequireSpan(entryOffset, width: width);
            entries.Add((T)Activator.CreateInstance(typeof(T), (nint)unit, entryOffset)!);
        }
        return entries;
    }

    private bool TryValue(int offset, out AtkValue value)
    {
        value = default;
        var absolute = (long)beginOffset + offset;
        if (absolute < 0 || absolute > int.MaxValue || unit == null || unit->AtkValues == null ||
            !BeastmasterReadBounds.Contains(unit->AtkValuesCount, (int)absolute))
            return false;
        value = unit->AtkValues[(int)absolute];
        return true;
    }

    private void RequireType(int offset, AtkValueType type)
    {
        if (!TryValue(offset, out var value) || value.Type != type)
            throw Unavailable($"Native field at offset {(long)beginOffset + offset} is not {type}.");
    }

    private void RequireString(int offset)
    {
        if (!TryValue(offset, out var value) ||
            value.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString or AtkValueType.WideString) ||
            value.String.Value == null)
            throw Unavailable($"Native string at offset {(long)beginOffset + offset} is unavailable.");
    }

    protected new uint? ReadUInt(int offset)
    {
        RequireType(offset, AtkValueType.UInt);
        return base.ReadUInt(offset);
    }

    protected new int? ReadInt(int offset)
    {
        RequireType(offset, AtkValueType.Int);
        return base.ReadInt(offset);
    }

    protected new bool? ReadBool(int offset)
    {
        RequireType(offset, AtkValueType.Bool);
        return base.ReadBool(offset);
    }

    protected new SeString ReadSeString(int offset)
    {
        RequireString(offset);
        return base.ReadSeString(offset);
    }

    protected new string ReadString(int offset)
    {
        RequireString(offset);
        return base.ReadString(offset);
    }

    protected bool TryReadUInt(int offset, out uint value)
    {
        value = 0;
        if (!TryValue(offset, out var native) || native.Type != AtkValueType.UInt)
            return false;
        value = native.UInt;
        return true;
    }
}
