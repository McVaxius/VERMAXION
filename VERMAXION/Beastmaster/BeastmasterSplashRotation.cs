using System;

namespace VERMAXION.Beastmaster;

internal sealed class BeastmasterSplashRotation
{
    internal const int Count = 50;
    private long nextChange;
    internal int Index { get; private set; } = -1;

    internal void Open(long now) => Advance(now);

    internal void Previous(long now) => Select((Index < 0 ? 0 : Index) + Count - 1, now);
    internal void Next(long now) => Select((Index < 0 ? -1 : Index) + 1, now);

    private void Select(int index, long now)
    {
        Index = index % Count;
        nextChange = now + 10_000;
    }

    internal int Current(long now)
    {
        if (Index < 0 || now >= nextChange) Advance(now);
        return Index;
    }

    private void Advance(long now)
    {
        Index = Index < 0 ? Random.Shared.Next(Count)
            : (Index + Random.Shared.Next(1, Count)) % Count;
        nextChange = now + 10_000;
    }
}
