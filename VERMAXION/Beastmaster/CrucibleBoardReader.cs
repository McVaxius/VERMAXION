// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f. Private local port.
using ECommons.GameHelpers;
using ECommons;

namespace VERMAXION.Beastmaster
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using Lumina.Excel;
    using Lumina.Excel.Sheets;

    public static class CrucibleBoardReader
    {
        private const int GridSpotEntry = 1;

        private const float MapCentre = 1024f;

        public static CrucibleBoard? Read(uint duty)
        {
            uint boardNumber = FindBoardNumber(duty);
            if (boardNumber == 0)
                return null;

            if (!GenericHelpers.TryGetRow(duty, out ContentFinderCondition dutyRow))
                return null;

            Dictionary<int, (CrucibleStopKind Kind, int Number)> kinds = ReadStopKinds(boardNumber);
            var battles = ReadBattleIds(boardNumber);
            var randomBattles = ReadRandomBattleIds(boardNumber, battles);
            Dictionary<int, Marker> markers = ReadMarkers(dutyRow.TerritoryType.Value.Map.Value);
            (Dictionary<int, GridSpot> spots, List<CrucibleBoardPath> paths) = ReadStopMap(boardNumber);

            if (kinds.Count == 0 || markers.Count == 0 || paths.Any(path => !spots.ContainsKey(path.From) || !spots.ContainsKey(path.To)))
                return null;

            List<CrucibleBoardStop> stops = [];
            foreach ((int id, GridSpot spot) in spots.OrderBy(x => x.Key))
            {
                (CrucibleStopKind kind, int number) = kinds.GetValueOrDefault(id, (CrucibleStopKind.Unknown, 0));

                Marker marker;
                if (kind == CrucibleStopKind.Start)
                {
                    if (EstimateStartPosition(spot, spots, markers) is not { } position)
                        return null;
                    marker = new Marker(position, 0);
                }
                else if (!markers.TryGetValue(id, out marker))
                    return null;

                stops.Add(new CrucibleBoardStop(id, kind, number, spot.X, spot.Y, marker.Position, marker.Icon)
                {
                    BattleId = kind is CrucibleStopKind.Enemy or CrucibleStopKind.Elite or CrucibleStopKind.Boss
                        ? battles.GetValueOrDefault(number) : 0,
                    RandomBattleIds = kind == CrucibleStopKind.Random ? randomBattles.GetValueOrDefault(number) : null,
                });
            }

            return new CrucibleBoard(boardNumber, dutyRow.Name.ExtractText(), stops, paths);
        }

        private static uint FindBoardNumber(uint duty)
        {
            foreach (XBMContent board in GenericHelpers.GetSheet<XBMContent>())
            {
                ushort boardDuty = board.Unknown33;
                if (board.RowId > 0 && boardDuty == duty)
                    return board.RowId;
            }

            return 0;
        }

        private static Dictionary<int, (CrucibleStopKind Kind, int Number)> ReadStopKinds(uint boardNumber)
        {
            Dictionary<int, (CrucibleStopKind, int)> kinds = [];

            if (!GenericHelpers.GetSubrowSheet<XBMContentStageEvent>().TryGetRow(boardNumber, out SubrowCollection<XBMContentStageEvent> stops))
                return kinds;

            foreach (XBMContentStageEvent stop in stops)
            {
                CrucibleStopKind kind   = (CrucibleStopKind)stop.Unknown0;
                int              number = stop.Unknown2;

                // campsite and chest start at 0
                if (kind is CrucibleStopKind.Campsite or CrucibleStopKind.Treasure)
                    number++;

                kinds[stop.SubrowId] = (kind, number);
            }

            return kinds;
        }

        private static Dictionary<int, uint> ReadBattleIds(uint boardNumber)
        {
            if (!GenericHelpers.GetSubrowSheet<XBMContentBattle>().TryGetRow(boardNumber, out SubrowCollection<XBMContentBattle> rows))
                return [];
            // Installed Lumina calls this ushort Unknown0; modern EXDSchema names it BattleDetail.
            return rows.ToDictionary(row => (int)row.SubrowId, row => (uint)row.Unknown0);
        }

        private static Dictionary<int, uint[]> ReadRandomBattleIds(uint boardNumber, Dictionary<int, uint> battles)
        {
            Dictionary<int, uint[]> result = [];
            if (!GenericHelpers.GetSubrowSheet<XBMContentRandomStageEvent>().TryGetRow(boardNumber,
                    out SubrowCollection<XBMContentRandomStageEvent> tables)) return result;
            foreach (var table in tables)
            {
                List<uint> candidates = [];
                if (!GenericHelpers.GetSubrowSheet<XBMRandomStageEvent>().TryGetRow(table.Unknown0,
                        out SubrowCollection<XBMRandomStageEvent> options))
                { result[table.SubrowId] = [0]; continue; }
                foreach (var option in options)
                {
                    var kind = (CrucibleStopKind)option.Unknown0;
                    if (kind is CrucibleStopKind.Enemy or CrucibleStopKind.Elite or CrucibleStopKind.Boss)
                        candidates.Add(battles.GetValueOrDefault(option.Unknown1));
                    else if (kind is not (CrucibleStopKind.Campsite or CrucibleStopKind.Treasure or CrucibleStopKind.Shop))
                        candidates.Add(0); // An unknown option cannot certify a single possible battle.
                }
                result[table.SubrowId] = candidates.ToArray();
            }
            return result;
        }

        private static (Dictionary<int, GridSpot> Spots, List<CrucibleBoardPath> Paths) ReadStopMap(uint boardNumber)
        {
            Dictionary<int, GridSpot> spots = [];
            List<CrucibleBoardPath>   paths = [];

            if (!GenericHelpers.GetSubrowSheet<XBMContentStageEventMap>().TryGetRow(boardNumber, out SubrowCollection<XBMContentStageEventMap> entries))
                return (spots, paths);

            foreach (XBMContentStageEventMap entry in entries)
            {
                int x         = entry.Unknown0;
                int y         = entry.Unknown1;
                int entryType = entry.Unknown2;
                int stop      = entry.Unknown3;
                int toStop    = entry.Unknown4;

                if (entryType == GridSpotEntry)
                    spots[stop] = new GridSpot(x, y);
                else
                    paths.Add(new CrucibleBoardPath(stop, toStop));
            }

            return (spots, paths);
        }

        private static Dictionary<int, Marker> ReadMarkers(Map map)
        {
            Dictionary<int, Marker> markers = [];

            if (!GenericHelpers.GetSubrowSheet<MapMarker>().TryGetRow(map.MapMarkerRange, out SubrowCollection<MapMarker> rows))
                return markers;

            float scale = map.SizeFactor / 100f;

            foreach (MapMarker row in rows)
            {
                if (row.Icon == 0)
                    continue;

                Vector2 position = new((row.X - MapCentre) / scale - map.OffsetX,
                                       (row.Y - MapCentre) / scale - map.OffsetY);

                markers[(int)row.DataKey.RowId] = new Marker(position, row.Icon);
            }

            return markers;
        }

        private static Vector2? EstimateStartPosition(GridSpot start, Dictionary<int, GridSpot> spots, Dictionary<int, Marker> markers)
        {
            List<float> aligned = markers.Where(m => spots.TryGetValue(m.Key, out GridSpot spot) && spot.X == start.X)
                                         .Select(m => m.Value.Position.X).ToList();

            List<float> nearestRows = markers.Where(m => spots.TryGetValue(m.Key, out GridSpot spot) && spot.Y < start.Y)
                                             .OrderByDescending(m => spots[m.Key].Y)
                                             .Select(m => m.Value.Position.Y)
                                             .Distinct()
                                             .Take(2)
                                             .ToList();

            if (aligned.Count == 0 || nearestRows.Count < 2)
                return null;

            float rowGap = nearestRows[0] - nearestRows[1];
            return new Vector2(aligned[0], nearestRows[0] + rowGap);
        }

        private readonly record struct GridSpot(int X, int Y);

        private readonly record struct Marker(Vector2 Position, uint Icon);
    }
}
