// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f. Private local port.
using ECommons.GameHelpers;
using ECommons.DalamudServices;

namespace VERMAXION.Beastmaster
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;

    public enum CrucibleStopKind
    {
        Unknown  = 0,
        Start    = 1,
        Enemy    = 2,
        Elite    = 3,
        Boss     = 4,
        Shop     = 5,
        Campsite = 6,
        Treasure = 7,
        Random   = 8,
    }

    public sealed record CrucibleBoardStop(int Id, CrucibleStopKind Kind, int Number, int GridX, int GridY, Vector2 Position, uint Icon)
    {
        // Number is the board-local sheet index, including valid boss index zero.
        // A random tile's possibilities are not its observed outcome.
        public uint BattleId { get; init; }
        public IReadOnlyList<uint>? RandomBattleIds { get; init; }
        public BeastmasterFightContext? ResolveFight(bool fightPanelReady)
            => BeastmasterSelection.ResolveFightBattle(BattleId, RandomBattleIds, fightPanelReady);
        public string Label => this.Number > 0 ? $"{this.Kind} #{this.Number}" : this.Kind.ToString();
    }

    public readonly record struct CrucibleBoardPath(int From, int To);

    public sealed class CrucibleBoard
    {
        private const float OnStopDistance = 12f;

        private static uint           cachedDuty;
        private static CrucibleBoard? cachedBoard;

        public uint                             BoardNumber { get; }
        public string                           Name        { get; }
        public IReadOnlyList<CrucibleBoardStop> Stops       { get; }
        public IReadOnlyList<CrucibleBoardPath> Paths       { get; }

        public CrucibleBoard(uint boardNumber, string name, List<CrucibleBoardStop> stops, List<CrucibleBoardPath> paths)
        {
            this.BoardNumber = boardNumber;
            this.Name        = name;
            this.Stops       = stops;
            this.Paths       = paths;
        }

        public static CrucibleBoard? Current()
        {
            uint duty = Svc.DutyState.ContentFinderCondition.RowId;
            if (duty != cachedDuty)
            {
                cachedDuty  = duty;
                cachedBoard = duty == 0 ? null : CrucibleBoardReader.Read(duty);
            }

            return cachedBoard;
        }

        public IEnumerable<CrucibleBoardStop> NextStops(CrucibleBoardStop stop) => 
            this.Paths.Where(x => x.From == stop.Id).Select(x => this.Stops.First(s => s.Id == x.To));

        public bool HasCampBeforeNextFight(CrucibleBoardStop stop, ref IEnumerable<CrucibleBoardStop> path)
        {
            if (stop.Kind == CrucibleStopKind.Campsite)
                return true;

            IEnumerable<CrucibleBoardStop> stops = this.NextStops(stop);
            foreach (CrucibleBoardStop cbs in stops)
            {
                IEnumerable<CrucibleBoardStop> tmpPath = [..path, cbs];
                if (cbs.Kind == CrucibleStopKind.Campsite || (cbs.Kind is not (CrucibleStopKind.Enemy or CrucibleStopKind.Elite or CrucibleStopKind.Boss or CrucibleStopKind.Random) && this.HasCampBeforeNextFight(cbs, ref tmpPath)))
                {
                    path = tmpPath;
                    return true;
                }
            }

            return false;
        }

        public CrucibleBoardStop? StopPlayerIsOn()
        {
            if (!Player.Available)
                return null;

            CrucibleBoardStop? nearest = this.Stops.MinBy(x => Player.DistanceTo(x.Position));
            if (nearest == null || Player.DistanceTo(nearest.Position) > OnStopDistance)
                return null;

            return nearest;
        }
    }
}
