using System;
using System.Collections.Generic;

namespace VERMAXION.Models;

public enum IslandVisitCadence { Weekly, Daily, Manual }

public enum IslandChore { Income, Agenda, Granary, Exports, Pasture, Crops, Seeds }

public sealed class IslandSanctuarySettings
{
    public IslandVisitCadence Cadence { get; set; } = IslandVisitCadence.Weekly;
    public bool Income { get; set; } = true;
    public bool Agenda { get; set; } = true;
    public bool Granary { get; set; } = true;
    public bool Exports { get; set; } = true;
    public bool Pasture { get; set; } = true;
    public bool Crops { get; set; } = true;
    public bool Seeds { get; set; } = true;

    public IslandSanctuarySettings Clone() => (IslandSanctuarySettings)MemberwiseClone();

    public bool IsSelected(IslandChore chore) => chore switch
    {
        IslandChore.Income => Income, IslandChore.Agenda => Agenda,
        IslandChore.Granary => Granary, IslandChore.Exports => Exports,
        IslandChore.Pasture => Pasture, IslandChore.Crops => Crops,
        IslandChore.Seeds => Seeds, _ => throw new ArgumentOutOfRangeException(nameof(chore)),
    };

    public void SetSelected(IslandChore chore, bool selected)
    {
        switch (chore)
        {
            case IslandChore.Income: Income = selected; break;
            case IslandChore.Agenda: Agenda = selected; break;
            case IslandChore.Granary: Granary = selected; break;
            case IslandChore.Exports: Exports = selected; break;
            case IslandChore.Pasture: Pasture = selected; break;
            case IslandChore.Crops: Crops = selected; break;
            case IslandChore.Seeds: Seeds = selected; break;
            default: throw new ArgumentOutOfRangeException(nameof(chore));
        }
    }
}

// Display contract only. There is deliberately no task, provider or completion state here.
public sealed record IslandRoutineStep(int Stage, IslandChore? Chore, string Label, string Detail, string Capability)
{
    public static readonly IReadOnlyList<IslandRoutineStep> Route = Array.AsReadOnly<IslandRoutineStep>(
    [
        new(1, null, "Visit island", "Required first. Island entry remains TBD.", "TBD"),
        new(2, IslandChore.Income, "Workshop income", "Collect earned cowries during the workshop visit.", "TBD"),
        new(2, IslandChore.Agenda, "Workshop agenda", "At the same workshop stop, use Calypso's plan. Full-week coverage is TBD.", "Partial"),
        new(3, IslandChore.Granary, "Granary mammets", "Collect expedition yields and send the mammets back out.", "TBD"),
        new(4, IslandChore.Exports, "Main building: sell excess stock", "Enter the main building and sell under Calypso's reserve rules.", "Existing"),
        new(5, IslandChore.Pasture, "Pasture care", "Collect leavings and care for the animals.", "TBD"),
        new(6, IslandChore.Crops, "Crop care", "Harvest, replant and care for the plants.", "TBD"),
        new(7, IslandChore.Seeds, "Seed replenishment: only if low", "Refill only low seed types under Calypso's stock and spending policy.", "TBD"),
    ]);
}
