using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace VERMAXION.Models;

public enum FishingExecutionMode
{
    CurrentCharacterOnly = 0,
    AutoRetainerRelogCurrentAccount = 1,
}

public enum FishingRunMode
{
    Scheduled = 0,
    Test = 1,
}

public enum OceanFishingRoutePreference
{
    Indigo = 0,
    Ruby = 1,
    Thavnair = 2,
}

public sealed class FishingRunContext
{
    public FishingRunMode Mode { get; init; }
    public string TargetCharacterKey { get; init; } = string.Empty;
    public DateTimeOffset RegistrationStartUtc { get; init; }
    public DateTimeOffset RegistrationDeadlineUtc { get; init; }
    public bool QueueRegistrationConfirmed { get; set; }
    public bool TerminalFailureBeforeQueueConfirmation { get; set; }
    public bool? InitialAutoRetainerMultiModeEnabled { get; set; }
    public bool AutoRetainerMultiModeChanged { get; set; }
    public bool? InitialAutoHookEnabled { get; set; }
    public bool AutoHookChanged { get; set; }
    public bool YesAlreadyLeaseOwned { get; set; }
    public bool YesAlreadyLeaseSuspendedForShopping { get; set; }
    public bool CleanupPending { get; set; }
    public string CleanupReason { get; set; } = string.Empty;
    public DateTimeOffset LastCleanupAttemptUtc { get; set; }

    public bool OwnsExternalState
        => AutoRetainerMultiModeChanged || AutoHookChanged || YesAlreadyLeaseOwned;
    public bool OwnsRegistrationLeases
        => AutoRetainerMultiModeChanged || YesAlreadyLeaseOwned;

    public string StatusPrefix => Mode == FishingRunMode.Test ? "Test: " : string.Empty;
}

public enum FishingReturnDestination
{
    None = 0,
    Home = 1,
    Limsa = 2,
    FreeCompany = 3,
    Custom = 4,
    Inn = 5,
}

public enum FishingRepairMode
{
    Disabled = 0,
    Self = 1,
    NpcNoInn = 2,
    NpcNoTeleportNoInn = 3,
}

public static class FishingDefaults
{
    public const FishingExecutionMode ExecutionMode = FishingExecutionMode.AutoRetainerRelogCurrentAccount;
    public const int MaxFisherLevel = 100;
    public const int OceanFishingPreWindowOffsetMinutes = -1;
    public const int MinOceanFishingPreWindowOffsetMinutes = -10;
    public const int MaxOceanFishingPreWindowOffsetMinutes = 0;
    public const int OceanFishingRegistrationIntervalHours = 2;
    public const int OceanFishingRegistrationAvailabilityMinutes = 15;
    public const int LureRestockTarget = 22;
    public const FishingReturnDestination ReturnDestination = FishingReturnDestination.Inn;
    public const string ReturnCommand = "/li inn";
    public const FishingRepairMode RepairMode = FishingRepairMode.NpcNoInn;
    public const int RepairThresholdPercent = 50;
}

// ArrivalClearance is the clearance tier the sampler ACCEPTED this point under — the arrival gate must
// re-verify at exactly that tier. Gating a preferred-tier point at the fallback floor would silently
// weaken the first-cast guard on quiet vessels; gating a fallback point at the full minimum livelocks.
public readonly record struct OceanFishingRailDestination(
    Vector3 Position,
    float Rotation,
    float ArrivalClearance);

/// <summary>
/// Discrete fixed-spot rail (mode 2): VMX positions toons only at an explicit operator-supplied list of
/// (Position, Rotation) points — the deck-rail live-test picks (24 primary + 8 backup) — instead of sampling
/// off geometry. Resolution mirrors the edge-spread policy over a finite set: settled-stay if my nearest spot
/// is clear, else the nearest listed spot that clears other players by the AoE, else the best-clearance
/// non-excluded spot. Facing is supplied per spot (the mapped outward-normal), so no sweep constants and no
/// deck-Y table — the supplied Y IS the arrival Y (must be within the 3D tolerance).
/// </summary>
internal static class OceanFishingDiscreteSpotPolicy
{
    public static bool Enabled;
    public static float PlayerAoeYalms = 2.0f;
    public static float HysteresisYalms = 0.3f;
    // Small so an excluded/dead spot poisons only itself, not deliberately-nearby backup spots (a sparse
    // hand-authored list, unlike the edge policy's dense ring where 1.5y is a tiny neighborhood).
    private const float ExclusionRadiusYalms = 0.5f;
    private static OceanFishingRailDestination[] _spots = Array.Empty<OceanFishingRailDestination>();

    /// <summary>The vessel's fishing spots, compiled in (world X/Y/Z + outward-facing rotation): 24 primary
    /// + 8 backup positions walked and validated on the live deck. The vessel's geometry is identical for
    /// every player and has not changed across expansions, so these are code, not configuration — no user
    /// ever authors coordinates, and no serialization can corrupt or drift them. Y is the measured deck
    /// floor (the arrival gate is 3D).</summary>
    private static readonly (float X, float Y, float Z, float Rotation)[] BuiltInSpots =
    {
        (6.75f, 5.21f, -17.53f, 1.7f),
        (-6.76f, 5.23f, -18.45f, -1.8692f),
        (7.21f, 5.25f, -15.96f, 1.7017f),
        (-7.29f, 5.25f, -15.93f, -1.5429f),
        (7.27f, 6.71f, -0.55f, 1.5708f),
        (-7.44f, 6.71f, -11.24f, -1.7226f),
        (7.3f, 6.75f, 2.99f, 1.5708f),
        (-7.27f, 6.71f, -0.82f, -1.5708f),
        (7.21f, 6.03f, 5.01f, 1.7471f),
        (-7.3f, 6.71f, 1.06f, -1.5708f),
        (6.93f, 5.25f, 6.91f, 1.8064f),
        (-7.3f, 6.75f, 3.2f, -1.5708f),
        (6.89f, 5.21f, 10.88f, 1.0821f),
        (-7.28f, 6.14f, 5.07f, -1.7261f),
        (8.04f, 5.79f, 14.71f, 1.5708f),
        (-6.9f, 5.25f, 6.77f, -1.9635f),
        (8.18f, 6.96f, 16.71f, 1.5708f),
        (-7.59f, 5.23f, 12.26f, -1.9076f),
        (-8.14f, 5.82f, 14.75f, -1.5708f),
        (-8.18f, 7.04f, 16.86f, -1.5708f),
        (-7.3f, 6.71f, -8.92f, -1.5708f),
        (-7.32f, 6.71f, -6.79f, -1.5708f),
        (-7.51f, 6.75f, -5.17f, -1.5708f),
        (-7.3f, 6.75f, -2.95f, -1.5708f),
        (7.5f, 6.07f, -13.86f, 1.5708f),
        (7.24f, 6.75f, -11.86f, 1.5708f),
        (7.46f, 6.71f, -10.21f, 1.5708f),
        (7.54f, 6.74f, -7.86f, 1.5708f),
        (7.45f, 6.75f, -5.86f, 1.5708f),
        (7.53f, 6.75f, -3.86f, 1.5708f),
        (7.3f, 6.71f, -1.86f, 1.5708f),
        (7.52f, 6.74f, 2.14f, 1.5708f),
    };
    // Avoid set: spots excluded (dead/contested) during the pre-lock positioning phase. The caller's
    // excludedDestination is ONE-SHOT (cleared on the next successful sample), which let a dead assigned spot
    // be re-selected on the following advance -> mine<->fallback churn. Accumulating it here keeps a bad spot
    // avoided until the set is cleared: at voyage entry (caller passes excludedDestination == null) and on any
    // config rebuild. NOTE: this policy is only consulted while PLACING the fisher; once fishing acknowledges,
    // MovementLocked holds the toon for the rest of the voyage, so the set is not re-read across the 3 zones
    // (a mid-voyage zone flip does NOT reset it, but also never queries it). Per-client (VMX is one instance
    // per client), only ever touched on the framework thread -> no locking.
    private static readonly HashSet<int> _avoidThisLeg = new();

    public static void ApplyConfiguration(Configuration configuration)
    {
        Enabled = configuration.OceanRailSpreadMode == 2;
        PlayerAoeYalms = Math.Clamp(configuration.OceanRailEdgePlayerAoeYalms, 0.5f, 5f);
        // Stamp the SAMPLER-GUARANTEED clearance tier (PlayerAoe), NOT max(Min, PlayerAoe): the sampler only
        // accepts a spot at the PlayerAoe tier, so stamping a higher arrival clearance it never enforced makes
        // the 3D arrival gate unreachable when Min > PlayerAoe -> full-voyage livelock.
        _spots = BuiltInSpots
            .Select(s => new OceanFishingRailDestination(new Vector3(s.X, s.Y, s.Z), s.Rotation, PlayerAoeYalms))
            .ToArray();
        // A rebuilt _spots invalidates the raw indices in the avoid set (a live ConfigWindow save re-runs this),
        // so drop them rather than avoid the wrong spots or fast-path a now-avoided assigned index.
        _avoidThisLeg.Clear();
    }

    private static float MinDistanceTo(Vector3 p, IReadOnlyList<Vector3> others)
    {
        var min = float.PositiveInfinity;
        for (var i = 0; i < others.Count; i++)
        {
            var dx = others[i].X - p.X;
            var dz = others[i].Z - p.Z;
            var d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < min) min = d;
        }
        return min;
    }

    public static bool TrySample(
        Vector3 myPosition,
        IReadOnlyList<Vector3> otherPlayerPositions,
        OceanFishingRailDestination? excludedDestination,
        out OceanFishingRailDestination destination)
    {
        destination = default;
        if (_spots.Length == 0)
            return false;

        // Maintain the per-leg avoid set. A leg re-entry (excludedDestination == null) clears
        // it; otherwise fold the excluded spot in so a dead/contested spot stays avoided for the rest of the
        // leg. This is the actual fix for the mine<->fallback churn: a position settled-stay could not hold,
        // because the caller re-excludes the CURRENT spot on every pre-lock advance, which dropped control back
        // to the fixed-assignment primary and re-selected the known-dead assigned spot.
        if (excludedDestination is null)
        {
            _avoidThisLeg.Clear();
        }
        else if (excludedDestination is { } ex)
        {
            for (var i = 0; i < _spots.Length; i++)
            {
                if (Vector2.Distance(new Vector2(_spots[i].Position.X, _spots[i].Position.Z),
                                     new Vector2(ex.Position.X, ex.Position.Z)) <= ExclusionRadiusYalms)
                {
                    _avoidThisLeg.Add(i);
                }
            }
        }

        // Settled-stay: a character already standing at a listed spot keeps it while the spot still clears
        // every player by the plain AoE radius. New picks below must clear AoE + hysteresis — the
        // asymmetric bands stop boundary jitter from re-triggering moves once placed.
        for (var i = 0; i < _spots.Length; i++)
        {
            if (_avoidThisLeg.Contains(i)) continue;
            var dxs = _spots[i].Position.X - myPosition.X;
            var dzs = _spots[i].Position.Z - myPosition.Z;
            if (dxs * dxs + dzs * dzs > 0.25f) continue;      // within 0.5y = standing on it
            if (MinDistanceTo(_spots[i].Position, otherPlayerPositions) >= PlayerAoeYalms)
            {
                destination = _spots[i];
                return true;
            }
            break;
        }

        // Nearest non-avoided spot clearing every other player by AoE + hysteresis. Since every
        // dead/contested spot is in the avoid set, the toon settles forward onto a fresh spot each
        // advance and CONVERGES, rather than ping-ponging back to a known-bad one.
        var clearNeeded = PlayerAoeYalms + HysteresisYalms;
        var best = -1;
        var bestDist = float.PositiveInfinity;
        for (var i = 0; i < _spots.Length; i++)
        {
            if (_avoidThisLeg.Contains(i)) continue;
            if (MinDistanceTo(_spots[i].Position, otherPlayerPositions) < clearNeeded) continue;
            var dx = _spots[i].Position.X - myPosition.X;
            var dz = _spots[i].Position.Z - myPosition.Z;
            var d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (best >= 0) { destination = _spots[best]; return true; }

        // Degrade like the other policies: best-clearance non-avoided spot rather than failing closed.
        var fb = -1;
        var fbClear = float.NegativeInfinity;
        for (var i = 0; i < _spots.Length; i++)
        {
            if (_avoidThisLeg.Contains(i)) continue;
            var c = MinDistanceTo(_spots[i].Position, otherPlayerPositions);
            if (c > fbClear) { fbClear = c; fb = i; }
        }
        if (fb < 0 || fbClear < OceanFishingContinuousRailPolicy.FallbackPlayerClearance)
        {
            // The whole list is avoided/blocked -> return false and let the caller's continuous-sampler
            // fallback place the character. The avoid set deliberately PERSISTS: clearing it here (the old
            // behavior, from before the fallback existed, when false meant idling the voyage) re-armed the
            // known-dead assigned spot after every sampler interlude, churning mine -> dead -> sampler ->
            // mine for the rest of the placement phase. Spots un-avoid at leg entry.
            return false;
        }
        destination = _spots[fb] with { ArrivalClearance = OceanFishingContinuousRailPolicy.FallbackPlayerClearance };
        return true;
    }
}

internal static class OceanFishingContinuousRailPolicy
{
    // Mutable statics rather than consts, deliberately: on a vessel whose population varies wildly these
    // are the knobs an operator needs to turn at runtime (via reflection tooling) without a rebuild.
    // Persisted values come from Configuration.OceanRail* via ApplyConfiguration at plugin load, so a
    // bare client is correctly tuned with no external tooling; runtime reflection can still override live.
    public static float MinimumPlayerClearance = 3f;
    public static float FallbackPlayerClearance = 1.5f;
    public static float RailStepYalms = 1f;

    // Direct-SetPosition offset applied at the settled fence to reach the TRUE deck edge, which navmesh
    // CANNOT walk to (it clamps ~1.5y inboard of the model edge). 0 = disabled (stay at the walkable edge,
    // prior behavior). Live-tunable via reflection like the clearance knobs, so it can be staged on one
    // client before wider rollout, and reverted instantly if the online instance rejects the warp.
    public static float EdgeSetPositionOffsetYalms = 0f;

    /// <summary>Angular speed of the CanFish-sampling facing sweep fallback, in degrees per second. The
    /// default matches a player holding the turn key (a full 360-degree circle in ~2s ≈ 180 deg/s); the
    /// sweep still snaps in 15-degree increments, this only sets how fast it steps through them.</summary>
    public static float FacingSweepDegreesPerSecond = 180f;

    /// <summary>Applies the persisted Configuration knob overrides. Call once at plugin load (and after
    /// any config-window edit of these values). Values are clamped to sane bounds so a hand-edited config
    /// cannot produce a zero step or a negative clearance.</summary>
    public static void ApplyConfiguration(Configuration configuration)
    {
        MinimumPlayerClearance = Math.Clamp(configuration.OceanRailMinimumPlayerClearance, 0.5f, 10f);
        FallbackPlayerClearance = Math.Clamp(configuration.OceanRailFallbackPlayerClearance, 0.5f, MinimumPlayerClearance);
        RailStepYalms = Math.Clamp(configuration.OceanRailStepYalms, 0.25f, 5f);
        FacingSweepDegreesPerSecond = Math.Clamp(configuration.OceanRailFacingSweepDegreesPerSecond, 30f, 720f);
        RailSliceCount = Math.Clamp(configuration.OceanRailSliceCount, 0, 64);
        RailSliceIndex = Math.Clamp(configuration.OceanRailSliceIndex, 0, Math.Max(0, RailSliceCount - 1));
    }
    public const float DeckY = 6.711f;
    public const float StarboardRotation = 1.5f;    // face +x (water off starboard)
    public const float PortRotation = -1.5f;         // face -x (water off port)
    public const float BowRotation = 0f;             // face +z (water off the bow)
    public const float SternRotation = 3.14159f;     // face -z (water off the stern)
    public const float FacingToleranceRadians = 0.05f;

    // Rotation is a SIMPLE fixed per-segment angle (perpendicular out from that rail), NOT a computed
    // face-away-from-centre vector. The face-away version gave DIAGONAL headings; at many spots the cast
    // then did not point at open water and the game reported CanFish=false, so clients thrashed resampling
    // (measured attempt counts reached 60-113 in a single voyage). A fixed perpendicular casts straight over the
    // water at every point on a side, which is how the original proven rail behaved.
    //
    // The full walkable perimeter, mapped by tracing a client around the deck with
    // collision on and reading settled positions (the physical walk, not pathfind success). Fields:
    // (MinZ, MaxZ, X nominal, Y deck height, Rotation). X is jittered +/-0.125y per point. Deck height
    // steps along each side (measured, y-consistent zones): mid deck 6.7, aft/forward-low walkways ~5.3,
    // raised foredeck ~7.5-8.25. Both bow z=27 and stern z=-26 were walk-confirmed at their limits.
    private static readonly (float MinZ, float MaxZ, float X, float Y, float Rotation)[] RailSegments =
    [
        // starboard, stern -> bow (continuous) — face +x
        (-16.0f, -13.0f, 7.0f, 5.6f, StarboardRotation),
        (-12.0f, 4.0f, 7.0f, 6.7f, StarboardRotation),
        (6.0f, 14.0f, 7.0f, 5.3f, StarboardRotation),
        (15.0f, 22.0f, 7.0f, 7.5f, StarboardRotation),
        // port, mid -> bow — face -x
        (-12.0f, 4.0f, -7.0f, 6.7f, PortRotation),
        (6.0f, 14.0f, -7.0f, 5.3f, PortRotation),
        (15.0f, 20.0f, -7.0f, 7.5f, PortRotation),
        // port-aft strip (separate, reached from the stern) — face -x
        (-19.0f, -14.0f, -6.6f, 5.0f, PortRotation),
        // bow (centreline, face +z) and stern — the stern now HUGS the walked fence taper (the old
        // constant x=3.0 across z -26..-19 sat up to ~2.7y inboard of the fence, so CanFish never
        // passed and clients thrashed there; walked settle points: 5.7@-21.7, 4.9@-23.4, 4.1@-24.7,
        // 2.6@-26.5). Two short segments track the taper ~0.5y inside it; the fence-push fallback closes
        // the remaining gap physically.
        (22.0f, 27.0f, 0.0f, 8.25f, BowRotation),
        (-24.0f, -21.0f, 4.3f, 5.2f, SternRotation),
        (-26.5f, -24.5f, 2.6f, 5.2f, SternRotation),
    ];

    // Per-client private rail slice. With many cooperating clients sampling SIMULTANEOUSLY, the clearance
    // check races: everyone still stands in the spawn stack, the whole rail looks empty to everyone, and
    // Independent random picks can collide when several clients arrive together. Slicing gives each client
    // a disjoint window of the linear rail, so peer spacing holds by construction regardless of timing:
    // candidates keep only the central 50% of each slice to preserve a gap between adjacent windows.
    // Defaults (0, 1) mean "whole rail" — behavior is unchanged until an operator assigns indices via
    // reflection tooling, exactly like the clearance knobs above. TrySample falls back to the whole rail
    // when the slice itself has no acceptable point (e.g. strangers parked in it), so a packed slice
    // degrades instead of stalling.
    public static int RailSliceIndex = 0;
    public static int RailSliceCount = 1;

    private static float TotalRailLength
    {
        get
        {
            var total = 0f;
            foreach (var (minZ, maxZ, _, _, _) in RailSegments)
                total += maxZ - minZ;
            return total;
        }
    }

    private static bool InSlice(float cumulative, int sliceIndex)
    {
        if (RailSliceCount <= 1)
            return true;
        var width = TotalRailLength / RailSliceCount;
        var lo = width * Math.Clamp(sliceIndex, 0, RailSliceCount - 1);
        return cumulative >= lo + width * 0.25f && cumulative <= lo + width * 0.75f;
    }

    /// <summary>
    /// Slice visit order for the fallback: own slice first, then neighbors by distance. Measured
    /// motivation: when a client's own slice was stranger-occupied, the old
    /// whole-rail fallback ignored slices entirely and could land inside another client's window. Borrowing
    /// the nearest free slice preserves the by-construction peer spacing even in the fallback.
    /// </summary>
    private static IEnumerable<int> SliceVisitOrder()
    {
        var own = Math.Clamp(RailSliceIndex, 0, Math.Max(0, RailSliceCount - 1));
        yield return own;
        for (var d = 1; d < RailSliceCount; d++)
        {
            if (own - d >= 0) yield return own - d;
            if (own + d < RailSliceCount) yield return own + d;
        }
    }

    /// <summary>
    /// Picks a rail point by sweeping every segment on BOTH sides rather than throwing random darts.
    /// </summary>
    /// <remarks>
    /// The previous implementation drew 32 independent random candidates and
    /// failed closed when all were blocked. On a busy vessel (measured: 23 other players) that exhausts
    /// routinely even when open rail exists, and several cooperating clients would then sit at
    /// "Waiting for an open rail point" indefinitely. The sweep enumerates the whole rail at
    /// <see cref="RailStepYalms"/> resolution (with a random phase and per-point jitter so repeated calls
    /// and concurrent clients do not contest identical points), then:
    /// 1. picks UNIFORMLY AT RANDOM among candidates with full <see cref="MinimumPlayerClearance"/> —
    ///    random choice, not best-clearance, so several clients sampling simultaneously spread out
    ///    instead of converging on the same "emptiest" spot;
    /// 2. otherwise falls back to the single largest-clearance point if it still clears
    ///    <see cref="FallbackPlayerClearance"/> — a busy boat degrades instead of stalling;
    /// 3. fails closed only when the entire rail is genuinely packed.
    /// </remarks>
    public static bool TrySample(
        Random random,
        IReadOnlyList<Vector3> otherPlayerPositions,
        OceanFishingRailDestination? previousDestination,
        out OceanFishingRailDestination destination)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(otherPlayerPositions);

        var players = otherPlayerPositions
            .Where(IsFinite)
            .ToArray();

        // RE-PICK (previousDestination present): the group is already anchored, so the per-client slice is
        // no longer needed to defeat the boarding-time stampede -- take the NEAREST valid spot on the WHOLE
        // rail to the one we're leaving, so a bumped client steps to the closest gap instead of walking the
        // ship end to end. Player clearance still enforces spacing.
        if (previousDestination is { } prev)
        {
            if (TryScan(random, players, previousDestination, prev.Position, -1, out destination))
                return true;
            destination = default;
            return false;
        }

        // INITIAL placement: slice-first, then NEIGHBOR slices by distance, then the whole rail as a last
        // resort, picking RANDOMLY within the winning slice. The private slice kills the concurrent-arrival
        // race between cooperating clients boarding at once.
        foreach (var slice in SliceVisitOrder())
        {
            if (TryScan(random, players, null, null, slice, out destination))
                return true;
        }
        if (RailSliceCount > 1 &&
            TryScan(random, players, null, null, -1, out destination))
            return true;

        destination = default;
        return false;
    }

    private static bool TryScan(
        Random random,
        Vector3[] players,
        OceanFishingRailDestination? previousDestination,
        Vector3? nearestTo,
        int sliceIndex,
        out OceanFishingRailDestination destination)
    {
        var clear = new List<OceanFishingRailDestination>();
        var best = default(OceanFishingRailDestination);
        var bestClearance = float.NegativeInfinity;

        foreach (var candidate in EnumerateRailCandidates(random, sliceIndex))
        {
            if (previousDestination is { } previous &&
                IsFinite(previous.Position) &&
                Vector3.Distance(candidate.Position, previous.Position) < MinimumPlayerClearance)
            {
                continue;
            }

            var clearance = ClearanceAt(candidate.Position, players);
            if (clearance >= MinimumPlayerClearance)
                clear.Add(candidate);
            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                best = candidate;
            }
        }

        if (clear.Count > 0)
        {
            // Re-pick takes the candidate NEAREST the spot we're leaving (shortest walk); initial placement
            // takes a random one so simultaneous boarders spread out instead of converging on one gap.
            var chosen = nearestTo is { } near
                ? clear.OrderBy(c => Vector3.DistanceSquared(c.Position, near)).First()
                : clear[random.Next(clear.Count)];
            destination = chosen with { ArrivalClearance = MinimumPlayerClearance };
            return true;
        }

        if (bestClearance >= FallbackPlayerClearance)
        {
            destination = best with { ArrivalClearance = FallbackPlayerClearance };
            return true;
        }

        destination = default;
        return false;
    }

    private static IEnumerable<OceanFishingRailDestination> EnumerateRailCandidates(Random random, int sliceIndex)
    {
        var step = MathF.Max(0.25f, RailStepYalms);
        var phase = (float)(random.NextDouble() * step);
        var offset = 0f;
        foreach (var (minZ, maxZ, segX, segY, rotation) in RailSegments)
        {
            for (var z = minZ + phase; z <= maxZ; z += step)
            {
                if (sliceIndex >= 0 && !InSlice(offset + (z - minZ), sliceIndex))
                    continue;
                var jitteredZ = Math.Clamp(z + Lerp(-0.3f, 0.3f, random.NextDouble()), minZ, maxZ);
                var x = segX + Lerp(-0.125f, 0.125f, random.NextDouble());
                yield return new OceanFishingRailDestination(
                    new Vector3(x, segY, jitteredZ),
                    rotation,
                    MinimumPlayerClearance);
            }
            offset += maxZ - minZ;
        }
    }

    private static float ClearanceAt(Vector3 position, IReadOnlyList<Vector3> players)
    {
        var minimum = float.PositiveInfinity;
        foreach (var player in players)
        {
            var distance = Vector3.Distance(position, player);
            if (distance < minimum)
                minimum = distance;
        }

        return minimum;
    }

    public static OceanFishingRailDestination SampleCandidate(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        // Pick a segment weighted by its length (longer rail = proportionally more likely), then a random
        // point in it, so the distribution is uniform along the whole rail as segments are added/resized.
        var target = random.NextDouble() * TotalRailLength;
        var (minZ, maxZ, segX, segY, rotation) = RailSegments[^1];
        var acc = 0f;
        foreach (var seg in RailSegments)
        {
            acc += seg.MaxZ - seg.MinZ;
            if (target <= acc) { (minZ, maxZ, segX, segY, rotation) = seg; break; }
        }
        var x = segX + Lerp(-0.125f, 0.125f, random.NextDouble());
        var z = Lerp(minZ, maxZ, random.NextDouble());
        return new OceanFishingRailDestination(
            new Vector3(x, segY, z),
            rotation,
            MinimumPlayerClearance);
    }

    public static bool HasPlayerClearance(
        Vector3 position,
        IReadOnlyList<Vector3> otherPlayerPositions)
        => HasPlayerClearance(position, otherPlayerPositions, MinimumPlayerClearance);

    // The explicit-clearance overload exists for the ARRIVAL gate: sampling prefers
    // MinimumPlayerClearance but may accept a fallback point down to FallbackPlayerClearance on a busy
    // vessel. If arrival then re-verified at the full minimum, a fallback destination could never pass —
    // the character would resample, walk, fail the gate again, and livelock without ever casting. The
    // gate therefore enforces OceanFishingRailDestination.ArrivalClearance — the tier the sampler
    // actually accepted that specific point under — so fallback points remain reachable while
    // preferred-tier points keep the full-minimum guard.
    public static bool HasPlayerClearance(
        Vector3 position,
        IReadOnlyList<Vector3> otherPlayerPositions,
        float clearance)
    {
        ArgumentNullException.ThrowIfNull(otherPlayerPositions);
        if (!IsFinite(position))
            return false;

        return otherPlayerPositions
            .Where(IsFinite)
            .All(player => Vector3.Distance(position, player) >= clearance);
    }

    public static bool IsFacingOutward(float currentRotation, float targetRotation)
    {
        if (!float.IsFinite(currentRotation) || !float.IsFinite(targetRotation))
            return false;

        var delta = MathF.IEEERemainder(currentRotation - targetRotation, MathF.Tau);
        return MathF.Abs(delta) <= FacingToleranceRadians;
    }

    private static bool IsFinite(Vector3 position)
        => float.IsFinite(position.X) &&
           float.IsFinite(position.Y) &&
           float.IsFinite(position.Z);

    private static float Lerp(float minimum, float maximum, double sample)
        => minimum + ((maximum - minimum) * (float)Math.Clamp(sample, 0d, 1d));
}

internal readonly record struct OceanFishingStartEvaluation(
    FishingCastDecision Decision,
    string Gate,
    bool StopNavigation);

internal readonly record struct OceanFishingPlacementEvaluation(
    bool Ready,
    bool ShouldResample,
    bool ShouldAbort,
    string Gate);

internal enum OceanFishingAdvanceReason
{
    None = 0,
    NavigationStalled = 1,
    NavigationTimeout = 2,
    CannotFish = 3,
    StartUnacknowledged = 4,
    PlayerClearanceLost = 5,
    FacingUnverified = 6,
}

internal sealed class OceanFishingVoyageState
{
    private DateTimeOffset? lastStartAttemptAt;
    private DateTimeOffset? lastRecoveryObservationAt;
    private DateTimeOffset? arrivalAt;
    private DateTimeOffset? lastFacingReapplyAt;
    private DateTimeOffset? pathStoppedAt;
    private DateTimeOffset? pathStatusUnavailableAt;
    private DateTimeOffset? facingUnverifiedAt;
    private TimeSpan destinationNavigationTime;
    private TimeSpan noProgressTime;
    private TimeSpan canFishFalseTime;
    private float bestDistance;
    private bool baitAppliedThisSession;

    public bool FishingEverStarted { get; private set; }
    public bool MovementLocked { get; private set; }
    public bool DestinationArrived { get; private set; }
    public bool PositioningActive { get; private set; }
    public int DestinationAttemptNumber { get; private set; }
    public int SessionNumber { get; private set; }
    public int SessionStartAttemptCount { get; private set; }
    public int PostArrivalStartAttemptCount { get; private set; }

    public static readonly TimeSpan NavigationStallDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FacingSettlementDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan FacingRetryInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan StoppedPathSettlementDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan PathStatusUnavailableTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FacingVerificationTimeout = TimeSpan.FromSeconds(10);
    public const float MinimumNavigationProgress = 0.25f;
    public const int PostArrivalAttemptLimit = 5;
    // Hard cap on how many distinct rail points one voyage will try before giving up. Without it,
    // AdvanceDestination resampled forever, so a client on a genuinely-unfishable spot ran around the deck
    // indefinitely on a public boat. At the cap the run abandons to a quiet logout rather
    // than keep performing.
    public const int MaxDestinationAttempts = 10;

    /// <summary>True once positioning has burned the whole attempt budget without ever starting to fish —
    /// the give-up signal that routes the voyage to a logout instead of another resample.</summary>
    public bool DestinationAttemptsExhausted =>
        PositioningActive && !MovementLocked && !FishingEverStarted &&
        DestinationAttemptNumber >= MaxDestinationAttempts;

    public void Reset()
    {
        lastStartAttemptAt = null;
        baitAppliedThisSession = false;
        FishingEverStarted = false;
        MovementLocked = false;
        PositioningActive = false;
        DestinationAttemptNumber = 0;
        SessionNumber = 0;
        SessionStartAttemptCount = 0;
        ResetDestinationRecovery(DateTimeOffset.MinValue);
    }

    public void BeginPositioning(DateTimeOffset nowUtc)
    {
        PositioningActive = true;
        DestinationAttemptNumber = 1;
        ResetDestinationRecovery(nowUtc);
    }

    public bool AdvanceDestination(DateTimeOffset nowUtc)
    {
        if (MovementLocked || FishingEverStarted || !PositioningActive)
            return false;
        if (DestinationAttemptNumber >= MaxDestinationAttempts)
            return false; // exhausted — caller routes to logout via DestinationAttemptsExhausted

        DestinationAttemptNumber++;
        ResetDestinationRecovery(nowUtc);
        return true;
    }

    public void BeginSession()
    {
        SessionNumber++;
        SessionStartAttemptCount = 0;
        lastStartAttemptAt = null;
        baitAppliedThisSession = false;
    }

    public bool TryApplySessionBait()
    {
        if (baitAppliedThisSession)
            return false;

        baitAppliedThisSession = true;
        return true;
    }

    public OceanFishingStartEvaluation EvaluateFishingStart(
        DateTimeOffset nowUtc,
        bool enabled,
        bool inFishingContext,
        bool zoneTransitionActive,
        bool playerAvailable,
        bool gatheringConditionActive,
        bool fishingConditionActive,
        bool resultWindowVisible,
        bool atDestination = false,
        bool initialPlacementReady = false,
        string initialPlacementGate = "")
    {
        if (!FishingEverStarted &&
            (!DestinationArrived || !atDestination || !initialPlacementReady))
        {
            return new OceanFishingStartEvaluation(
                FishingCastDecision.Suppressed,
                string.IsNullOrWhiteSpace(initialPlacementGate)
                    ? "waiting for verified rail placement"
                    : initialPlacementGate,
                StopNavigation: false);
        }

        var evaluation = FishingCastPolicy.Evaluate(
            enabled,
            inFishingContext,
            zoneTransitionActive,
            playerAvailable,
            gatheringConditionActive,
            fishingConditionActive,
            resultWindowVisible,
            lastStartAttemptAt.HasValue
                ? nowUtc - lastStartAttemptAt.Value
                : TimeSpan.MaxValue);

        if (evaluation.Decision == FishingCastDecision.Attempt)
        {
            lastStartAttemptAt = nowUtc;
            SessionStartAttemptCount++;
            if (atDestination && DestinationArrived)
                PostArrivalStartAttemptCount++;
        }

        var stopNavigation = false;
        if (evaluation.Decision == FishingCastDecision.Acknowledged)
        {
            stopNavigation = !MovementLocked;
            FishingEverStarted = true;
            MovementLocked = true;
        }

        return new OceanFishingStartEvaluation(
            evaluation.Decision,
            evaluation.Gate,
            stopNavigation);
    }

    public void PauseRecovery(DateTimeOffset nowUtc)
    {
        lastRecoveryObservationAt = nowUtc;
        ResetPlacementVerification();
    }

    public void MarkArrived(DateTimeOffset nowUtc)
    {
        if (DestinationArrived)
            return;

        DestinationArrived = true;
        arrivalAt = nowUtc;
        lastFacingReapplyAt = null;
        canFishFalseTime = TimeSpan.Zero;
        PostArrivalStartAttemptCount = 0;
        lastRecoveryObservationAt = nowUtc;
        ResetPlacementVerification();
    }

    public OceanFishingPlacementEvaluation EvaluatePlacementReadiness(
        DateTimeOffset nowUtc,
        bool inFishingContext,
        bool zoneTransitionActive,
        bool playerAvailable,
        bool timersPaused,
        bool atDestination,
        bool playerClear,
        bool pathStatusAvailable,
        bool pathRunning,
        bool facingVerified)
    {
        if (FishingEverStarted || MovementLocked)
            return ReadyPlacement();

        if (!PositioningActive)
        {
            ResetPlacementVerification();
            return WaitingPlacement("voyage positioning inactive");
        }

        if (!inFishingContext)
        {
            ResetPlacementVerification();
            return WaitingPlacement("Ocean Fishing duty context inactive");
        }

        if (zoneTransitionActive)
        {
            ResetPlacementVerification();
            return WaitingPlacement("route transition active");
        }

        if (!playerAvailable)
        {
            ResetPlacementVerification();
            return WaitingPlacement("player unavailable");
        }

        if (timersPaused)
        {
            ResetPlacementVerification();
            return WaitingPlacement("placement verification paused by unsafe player state");
        }

        if (!atDestination)
        {
            ResetPlacementVerification();
            return WaitingPlacement("waiting to reach continuous rail destination");
        }

        if (!playerClear)
        {
            ResetPlacementVerification();
            return new OceanFishingPlacementEvaluation(
                Ready: false,
                ShouldResample: true,
                ShouldAbort: false,
                "another player is inside the destination's first-cast clearance");
        }

        if (!DestinationArrived)
        {
            ResetPlacementVerification();
            return WaitingPlacement("waiting for arrival stop and facing application");
        }

        if (!pathStatusAvailable)
        {
            pathStoppedAt = null;
            facingUnverifiedAt = null;
            pathStatusUnavailableAt ??= nowUtc;
            var timedOut = nowUtc - pathStatusUnavailableAt.Value >= PathStatusUnavailableTimeout;
            return new OceanFishingPlacementEvaluation(
                Ready: false,
                ShouldResample: false,
                ShouldAbort: timedOut,
                timedOut
                    ? "vnavmesh path status unavailable for 10 active seconds"
                    : "waiting for vnavmesh path status");
        }

        pathStatusUnavailableAt = null;
        if (pathRunning)
        {
            pathStoppedAt = null;
            facingUnverifiedAt = null;
            return WaitingPlacement("waiting for vnavmesh movement to stop");
        }

        pathStoppedAt ??= nowUtc;
        if (nowUtc - pathStoppedAt.Value < StoppedPathSettlementDelay)
        {
            facingUnverifiedAt = null;
            return WaitingPlacement("waiting for one continuous stopped second");
        }

        if (!facingVerified)
        {
            facingUnverifiedAt ??= nowUtc;
            var timedOut = nowUtc - facingUnverifiedAt.Value >= FacingVerificationTimeout;
            return new OceanFishingPlacementEvaluation(
                Ready: false,
                ShouldResample: timedOut,
                ShouldAbort: false,
                timedOut
                    ? "outward character facing did not verify for 10 active seconds"
                    : "waiting for outward character facing readback");
        }

        facingUnverifiedAt = null;
        return ReadyPlacement();
    }

    public bool ShouldReapplyFacing(DateTimeOffset nowUtc)
    {
        if (!DestinationArrived || FishingEverStarted || MovementLocked ||
            !arrivalAt.HasValue || nowUtc - arrivalAt.Value < FacingSettlementDelay)
        {
            return false;
        }

        if (lastFacingReapplyAt.HasValue &&
            nowUtc - lastFacingReapplyAt.Value < FacingRetryInterval)
        {
            return false;
        }

        lastFacingReapplyAt = nowUtc;
        return true;
    }

    public OceanFishingAdvanceReason EvaluateRecovery(
        DateTimeOffset nowUtc,
        float distance,
        bool atDestination,
        bool canFish,
        bool timersPaused)
    {
        if (FishingEverStarted || MovementLocked || !PositioningActive)
            return OceanFishingAdvanceReason.None;

        var delta = TimeSpan.Zero;
        if (lastRecoveryObservationAt.HasValue && nowUtc > lastRecoveryObservationAt.Value)
            delta = nowUtc - lastRecoveryObservationAt.Value;
        lastRecoveryObservationAt = nowUtc;

        if (timersPaused)
            return OceanFishingAdvanceReason.None;

        if (atDestination)
        {
            if (!DestinationArrived)
            {
                MarkArrived(nowUtc);
                delta = TimeSpan.Zero;
            }

            if (canFish)
                canFishFalseTime = TimeSpan.Zero;
            else
                canFishFalseTime += delta;

            if (canFishFalseTime >= FishingCastPolicy.CanFishFallbackDelay)
                return OceanFishingAdvanceReason.CannotFish;
            if (PostArrivalStartAttemptCount >= PostArrivalAttemptLimit)
                return OceanFishingAdvanceReason.StartUnacknowledged;

            return OceanFishingAdvanceReason.None;
        }

        if (DestinationArrived)
        {
            ResetDestinationRecovery(nowUtc);
            bestDistance = distance;
            return OceanFishingAdvanceReason.None;
        }

        destinationNavigationTime += delta;
        if (!float.IsFinite(bestDistance))
        {
            bestDistance = distance;
        }
        else if (float.IsFinite(distance) && bestDistance - distance >= MinimumNavigationProgress)
        {
            bestDistance = distance;
            noProgressTime = TimeSpan.Zero;
        }
        else
        {
            noProgressTime += delta;
        }

        if (noProgressTime >= NavigationStallDelay)
            return OceanFishingAdvanceReason.NavigationStalled;
        if (destinationNavigationTime >= NavigationTimeout)
            return OceanFishingAdvanceReason.NavigationTimeout;

        return OceanFishingAdvanceReason.None;
    }

    private void ResetDestinationRecovery(DateTimeOffset nowUtc)
    {
        lastRecoveryObservationAt = nowUtc == DateTimeOffset.MinValue ? null : nowUtc;
        arrivalAt = null;
        lastFacingReapplyAt = null;
        ResetPlacementVerification();
        destinationNavigationTime = TimeSpan.Zero;
        noProgressTime = TimeSpan.Zero;
        canFishFalseTime = TimeSpan.Zero;
        bestDistance = float.PositiveInfinity;
        DestinationArrived = false;
        PostArrivalStartAttemptCount = 0;
    }

    private void ResetPlacementVerification()
    {
        pathStoppedAt = null;
        pathStatusUnavailableAt = null;
        facingUnverifiedAt = null;
    }

    private static OceanFishingPlacementEvaluation WaitingPlacement(string gate)
        => new(
            Ready: false,
            ShouldResample: false,
            ShouldAbort: false,
            gate);

    private static OceanFishingPlacementEvaluation ReadyPlacement()
        => new(
            Ready: true,
            ShouldResample: false,
            ShouldAbort: false,
            string.Empty);
}

public readonly record struct OceanFishingStartupWindow(
    DateTimeOffset RegistrationStartUtc,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public readonly record struct OceanFishingRegistrationWindow(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public static class OceanFishingSchedulePolicy
{
    public static int NormalizePreWindowOffsetMinutes(int offsetMinutes)
        => Math.Clamp(
            offsetMinutes,
            FishingDefaults.MinOceanFishingPreWindowOffsetMinutes,
            FishingDefaults.MaxOceanFishingPreWindowOffsetMinutes);

    public static bool IsStartupWindowActive(DateTimeOffset nowUtc, int preWindowOffsetMinutes)
        => TryGetActiveStartupWindow(nowUtc, preWindowOffsetMinutes, out _);

    public static string DescribeInactiveStartupWindow(DateTimeOffset nowUtc, int preWindowOffsetMinutes)
    {
        var normalizedNow = nowUtc.ToUniversalTime();
        var nextRegistrationStart = GetNextRegistrationStart(normalizedNow);
        var nextWindow = BuildStartupWindow(nextRegistrationStart, preWindowOffsetMinutes);
        return $"No Ocean Fishing startup gate is active at {normalizedNow:u}; " +
               $"next gate is {nextWindow.StartUtc:u} until {nextWindow.EndUtc:u} (end exclusive).";
    }

    public static bool TryGetActiveStartupWindow(
        DateTimeOffset nowUtc,
        int preWindowOffsetMinutes,
        out OceanFishingStartupWindow window)
    {
        var normalizedNow = nowUtc.ToUniversalTime();
        foreach (var registrationStart in GetCandidateRegistrationStarts(normalizedNow))
        {
            var candidate = BuildStartupWindow(registrationStart, preWindowOffsetMinutes);
            if (normalizedNow >= candidate.StartUtc && normalizedNow < candidate.EndUtc)
            {
                window = candidate;
                return true;
            }
        }

        window = default;
        return false;
    }

    public static OceanFishingStartupWindow BuildStartupWindow(
        DateTimeOffset registrationStartUtc,
        int preWindowOffsetMinutes)
    {
        var normalizedRegistrationStart = registrationStartUtc.ToUniversalTime();
        var normalizedOffset = NormalizePreWindowOffsetMinutes(preWindowOffsetMinutes);
        return new OceanFishingStartupWindow(
            normalizedRegistrationStart,
            normalizedRegistrationStart.AddMinutes(normalizedOffset),
            normalizedRegistrationStart.AddMinutes(FishingDefaults.OceanFishingRegistrationAvailabilityMinutes));
    }

    public static OceanFishingRegistrationWindow BuildRegistrationWindow(DateTimeOffset registrationStartUtc)
    {
        var normalizedRegistrationStart = registrationStartUtc.ToUniversalTime();
        return new OceanFishingRegistrationWindow(
            normalizedRegistrationStart,
            normalizedRegistrationStart.AddMinutes(FishingDefaults.OceanFishingRegistrationAvailabilityMinutes));
    }

    public static OceanFishingRegistrationWindow GetCurrentOrNextRegistrationWindow(DateTimeOffset nowUtc)
        => BuildRegistrationWindow(GetNextRegistrationStart(nowUtc.ToUniversalTime()));

    private static DateTimeOffset GetNextRegistrationStart(DateTimeOffset nowUtc)
    {
        var hourStart = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            nowUtc.Hour,
            0,
            0,
            TimeSpan.Zero);
        var currentEvenHour = hourStart.Hour % FishingDefaults.OceanFishingRegistrationIntervalHours == 0
            ? hourStart
            : hourStart.AddHours(1);

        return nowUtc < BuildStartupWindow(currentEvenHour, 0).EndUtc
            ? currentEvenHour
            : currentEvenHour.AddHours(FishingDefaults.OceanFishingRegistrationIntervalHours);
    }

    private static IEnumerable<DateTimeOffset> GetCandidateRegistrationStarts(DateTimeOffset nowUtc)
    {
        var hourStart = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            nowUtc.Hour,
            0,
            0,
            TimeSpan.Zero);
        var currentOrPreviousEvenHour = hourStart.Hour % FishingDefaults.OceanFishingRegistrationIntervalHours == 0
            ? hourStart
            : hourStart.AddHours(-1);

        yield return currentOrPreviousEvenHour.AddHours(-FishingDefaults.OceanFishingRegistrationIntervalHours);
        yield return currentOrPreviousEvenHour;
        yield return currentOrPreviousEvenHour.AddHours(FishingDefaults.OceanFishingRegistrationIntervalHours);
    }
}

public readonly record struct FishingOperationSettings(
    int LureRestockTarget,
    FishingReturnDestination ReturnDestination,
    string ReturnCommand,
    FishingRepairMode RepairMode,
    int RepairThresholdPercent);

public readonly record struct FishingCharacterCandidate(
    string CharacterKey,
    int? FisherLevel,
    bool FishingEnabled,
    bool AlwaysFishIfWindowOpen,
    bool IsCurrentCharacter);

public sealed record FishingSelectionResult(
    string CharacterKey,
    int? FisherLevel,
    bool RequiresRelog,
    IReadOnlyList<string> AlwaysFishKeysToDisable,
    string Reason,
    bool AlwaysFishOverride = false)
{
    public bool Selected => !string.IsNullOrWhiteSpace(CharacterKey);

    public static FishingSelectionResult None(string reason)
        => new(string.Empty, null, false, Array.Empty<string>(), reason);
}

public readonly record struct FishingLiveFisherLevelSnapshot(
    bool IsAvailable,
    int Level,
    string Detail)
{
    public static FishingLiveFisherLevelSnapshot Ready(int level)
        => new(true, Math.Max(0, level), string.Empty);

    public static FishingLiveFisherLevelSnapshot Unavailable(string detail)
        => new(false, 0, detail);
}

public static class FishingXadbCandidatePolicy
{
    public static IReadOnlyList<FishingCharacterCandidate> ApplyAuthoritativeLevels(
        IEnumerable<FishingCharacterCandidate> configuredCandidates,
        XaFishingRosterSnapshot roster)
    {
        if (!roster.IsUsable)
            return Array.Empty<FishingCharacterCandidate>();

        var levels = roster.Characters.ToDictionary(
            entry => entry.CharacterKey,
            entry => entry.FisherLevel,
            StringComparer.OrdinalIgnoreCase);
        return configuredCandidates
            .Select(candidate => candidate with
            {
                FisherLevel = levels.TryGetValue(candidate.CharacterKey.Trim(), out var fisherLevel)
                    ? fisherLevel
                    : null,
            })
            .ToArray();
    }
}

public static class FishingStartupPolicy
{
    public static FishingSelectionResult SelectStartupTarget(
        IEnumerable<FishingCharacterCandidate> candidates,
        int maxFisherLevel,
        FishingExecutionMode mode,
        string currentCharacterKey,
        bool startupWindowActive)
    {
        if (!startupWindowActive)
            return FishingSelectionResult.None("No VERMAXION Ocean Fishing startup window is active.");

        return FishingSelectionPolicy.Select(
            candidates,
            maxFisherLevel,
            mode,
            currentCharacterKey,
            fishingWindowActive: true);
    }

    public static bool ShouldStartOnCurrentCharacter(FishingSelectionResult selection, string currentCharacterKey)
        => selection.Selected &&
           !selection.RequiresRelog &&
           string.Equals(selection.CharacterKey, currentCharacterKey, StringComparison.OrdinalIgnoreCase);
}

public static class FishingSelectionPolicy
{
    public static IReadOnlyList<FishingSelectionResult> BuildOrderedCandidates(
        IEnumerable<FishingCharacterCandidate> candidates,
        int maxFisherLevel,
        FishingExecutionMode mode,
        string currentCharacterKey,
        bool fishingWindowActive,
        IReadOnlySet<string>? excludedCharacterKeys = null)
    {
        var normalizedCurrentKey = NormalizeKey(currentCharacterKey);
        var excluded = excludedCharacterKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedCandidates = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.CharacterKey))
            .Select(candidate => candidate with
            {
                CharacterKey = NormalizeKey(candidate.CharacterKey),
                FisherLevel = candidate.FisherLevel.HasValue
                    ? Math.Max(0, candidate.FisherLevel.Value)
                    : null,
                IsCurrentCharacter = string.Equals(
                    NormalizeKey(candidate.CharacterKey),
                    normalizedCurrentKey,
                    StringComparison.OrdinalIgnoreCase),
            })
            .GroupBy(candidate => candidate.CharacterKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.IsCurrentCharacter)
                .ThenByDescending(candidate => candidate.FishingEnabled)
                .ThenByDescending(candidate => candidate.FisherLevel.HasValue)
                .First())
            .Where(candidate => candidate.FishingEnabled && !excluded.Contains(candidate.CharacterKey))
            .ToList();

        var cappedMaxLevel = Math.Clamp(maxFisherLevel, 1, 100);
        if (mode == FishingExecutionMode.CurrentCharacterOnly)
        {
            var current = normalizedCandidates.FirstOrDefault(candidate => candidate.IsCurrentCharacter);
            if (string.IsNullOrWhiteSpace(current.CharacterKey) ||
                !IsLevelEligible(current, cappedMaxLevel, fishingWindowActive))
            {
                return Array.Empty<FishingSelectionResult>();
            }

            return
            [
                BuildResult(
                    current,
                    requiresRelog: false,
                    "Selected current character.",
                    fishingWindowActive && current.AlwaysFishIfWindowOpen),
            ];
        }

        var ordered = normalizedCandidates
            .Where(candidate =>
                fishingWindowActive && candidate.AlwaysFishIfWindowOpen ||
                candidate.FisherLevel.HasValue && candidate.FisherLevel.Value < cappedMaxLevel)
            .OrderByDescending(candidate => fishingWindowActive && candidate.AlwaysFishIfWindowOpen)
            .ThenBy(candidate => candidate.FisherLevel ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.IsCurrentCharacter)
            .ThenBy(candidate => candidate.CharacterKey, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => BuildResult(
                candidate,
                requiresRelog: !candidate.IsCurrentCharacter,
                fishingWindowActive && candidate.AlwaysFishIfWindowOpen
                    ? "Selected always-fish character for active fishing window."
                    : "Selected lowest known Fisher below max.",
                fishingWindowActive && candidate.AlwaysFishIfWindowOpen))
            .ToArray();

        return ordered;
    }

    public static FishingSelectionResult Select(
        IEnumerable<FishingCharacterCandidate> candidates,
        int maxFisherLevel,
        FishingExecutionMode mode,
        string currentCharacterKey,
        bool fishingWindowActive)
    {
        var ordered = BuildOrderedCandidates(
            candidates,
            maxFisherLevel,
            mode,
            currentCharacterKey,
            fishingWindowActive);
        return ordered.Count > 0
            ? ordered[0]
            : FishingSelectionResult.None(
                mode == FishingExecutionMode.CurrentCharacterOnly
                    ? "Current character is disabled, unknown, or at the configured Fisher cap."
                    : $"No enabled configured character has a known Fisher below max {Math.Clamp(maxFisherLevel, 1, 100)}, and no AlwaysFish override applies.");
    }

    private static bool IsLevelEligible(FishingCharacterCandidate candidate, int maxFisherLevel, bool fishingWindowActive)
        => candidate.FisherLevel.HasValue && candidate.FisherLevel.Value < maxFisherLevel ||
           fishingWindowActive && candidate.AlwaysFishIfWindowOpen;

    private static FishingSelectionResult BuildResult(
        FishingCharacterCandidate selected,
        bool requiresRelog,
        string reason,
        bool alwaysFishOverride)
    {
        return new FishingSelectionResult(
            selected.CharacterKey,
            selected.FisherLevel,
            requiresRelog,
            Array.Empty<string>(),
            reason,
            alwaysFishOverride);
    }

    private static string NormalizeKey(string value)
        => value.Trim();
}

public enum FishingAttemptFailureKind
{
    CharacterPermanent,
    SharedTransient,
    Stop,
}

public static class FishingRecoveryPolicy
{
    public const int MaximumTransientRetries = 2;
    public static readonly TimeSpan MinimumAttemptTimeRemaining = TimeSpan.FromSeconds(60);

    public static TimeSpan GetTransientBackoff(int retryNumber)
        => retryNumber switch
        {
            1 => TimeSpan.FromSeconds(3),
            2 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.MaxValue,
        };

    public static bool CanStartAttempt(DateTimeOffset nowUtc, DateTimeOffset registrationDeadlineUtc)
        => registrationDeadlineUtc.ToUniversalTime() - nowUtc.ToUniversalTime() >= MinimumAttemptTimeRemaining;

    public static bool MayRecover(
        FishingAttemptFailureKind failureKind,
        bool queueConfirmed,
        bool registrationOpen,
        int transientRetriesAlreadyScheduled)
        => !queueConfirmed &&
           registrationOpen &&
           failureKind switch
           {
               FishingAttemptFailureKind.CharacterPermanent => true,
               FishingAttemptFailureKind.SharedTransient => transientRetriesAlreadyScheduled < MaximumTransientRetries,
               _ => false,
           };
}

public enum FishingCleanupCommand
{
    None,
    Discard,
    Sell,
}

public static class FishingInventoryCleanupPolicy
{
    public static IReadOnlyList<FishingCleanupCommand> Build(bool discardEnabled, bool sellEnabled)
    {
        var result = new List<FishingCleanupCommand>(2);
        if (discardEnabled)
            result.Add(FishingCleanupCommand.Discard);
        if (sellEnabled)
            result.Add(FishingCleanupCommand.Sell);
        return result;
    }

    public static bool TreatAsNothingToProcess(bool busyObserved, TimeSpan elapsed)
        => !busyObserved && elapsed >= TimeSpan.FromSeconds(10);
}

public static class FishingReturnPolicy
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FailAfter = TimeSpan.FromSeconds(120);

    // A territory change alone must NOT verify the return: multi-hop returns (city aetheryte -> inn room)
    // change territory mid-chain, and settling there ran cleanup + multi-restore while Lifestream was still
    // traveling. Any positive signal counts only once Lifestream and
    // the transition conditions are quiet.
    public static bool IsVerified(bool commandRequired, bool activityObserved, bool territoryChanged, bool currentlyBusy)
        => !commandRequired || !currentlyBusy && (territoryChanged || activityObserved);

    // The 30s single retry must also not fire while the first command's chain is still executing —
    // re-sending /li inn mid-chain stacks a duplicate task queue.
    public static bool ShouldRetry(int commandsSent, TimeSpan elapsed)
        => commandsSent == 1 && elapsed >= RetryAfter;

    public static bool ShouldRetry(int commandsSent, TimeSpan elapsed, bool currentlyBusy)
        => !currentlyBusy && ShouldRetry(commandsSent, elapsed);

    public static bool ShouldSuppressCommand(bool resultAddonVisible)
        => resultAddonVisible;
}

public static class XaFishingRosterParser
{
    private const int FisherJobId = 18;
    private const int MinimumIpcContractVersion = 6;

    public static XaFishingRosterSnapshot Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return XaFishingRosterSnapshot.Failure(
                XaFishingRosterReadStatus.EmptyResponse,
                "XA Database returned an empty account roster response.");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Malformed("The account roster root is not an object.");

            var generatedAtUtc = TryGetDateTimeOffset(root, "generatedAtUtc", out var generatedAt)
                ? generatedAt
                : null;
            if (!TryGetProperty(root, "ipcContractVersion", out var contractVersion) ||
                !TryReadInt(contractVersion, out var ipcContractVersion))
            {
                return UnsupportedContract(
                    "XA Database account roster IPC must include numeric ipcContractVersion; XADB 0.0.0.39+ contract v6 is required.",
                    generatedAtUtc);
            }

            if (ipcContractVersion < MinimumIpcContractVersion)
            {
                return UnsupportedContract(
                    $"XA Database account roster IPC contract v{ipcContractVersion} is unsupported; XADB 0.0.0.39+ contract v6 is required.",
                    generatedAtUtc);
            }

            if (!TryGetProperty(root, "isFullRosterAvailable", out var fullRoster) ||
                fullRoster.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Malformed("The account roster does not contain a boolean isFullRosterAvailable status.");
            }

            if (!fullRoster.GetBoolean())
            {
                return new XaFishingRosterSnapshot(
                    XaFishingRosterReadStatus.FullRosterUnavailable,
                    generatedAtUtc,
                    Array.Empty<XaFishingRosterEntry>(),
                    ReadWarnings(root, "XA Database contract v6 roster IPC did not advertise a full account roster."));
            }

            if (!TryGetProperty(root, "characters", out var characters) ||
                characters.ValueKind != JsonValueKind.Array)
            {
                return Malformed("The full account roster does not contain a characters array.", generatedAtUtc);
            }

            var result = new List<XaFishingRosterEntry>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var character in characters.EnumerateArray())
            {
                if (character.ValueKind != JsonValueKind.Object)
                    return Malformed("The account roster contains a non-object character row.", generatedAtUtc);

                var key = GetCharacterKey(character);
                if (string.IsNullOrWhiteSpace(key))
                    return Malformed("The account roster contains a character row without a character key.", generatedAtUtc);
                if (!seenKeys.Add(key))
                    return Malformed($"The account roster contains duplicate character key '{key}'.", generatedAtUtc);

                if (!TryGetFisherLevel(character, out var fisherLevel, out var levelError))
                    return Malformed($"{key}: {levelError}", generatedAtUtc);

                DateTimeOffset? snapshotTimestamp = null;
                if (TryGetProperty(
                        character,
                        out var snapshotElement,
                        "lastSnapshotUtc",
                        "snapshotUtc",
                        "updatedUtc",
                        "capturedAtUtc",
                        "lastSaveUtc"))
                {
                    if (!TryReadDateTimeOffset(snapshotElement, out var parsedTimestamp))
                        return Malformed($"{key}: snapshot timestamp is malformed.", generatedAtUtc);
                    snapshotTimestamp = parsedTimestamp;
                }

                result.Add(new XaFishingRosterEntry(
                    key,
                    fisherLevel,
                    GetString(character, "source").Trim(),
                    snapshotTimestamp));
            }

            return new XaFishingRosterSnapshot(
                XaFishingRosterReadStatus.Ready,
                generatedAtUtc,
                result,
                ReadWarnings(root, "XA Database full account roster is ready."));
        }
        catch (JsonException ex)
        {
            return Malformed($"Account roster JSON is malformed: {ex.Message}");
        }
    }

    private static string GetCharacterKey(JsonElement character)
    {
        var characterKey = GetString(character, "characterKey");
        if (!string.IsNullOrWhiteSpace(characterKey))
            return characterKey.Trim();

        var characterName = GetString(character, "characterName");
        var worldName = GetString(character, "worldName");
        return string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(worldName)
            ? string.Empty
            : $"{characterName.Trim()}@{worldName.Trim()}";
    }

    private static bool TryGetFisherLevel(
        JsonElement character,
        out int? level,
        out string error)
    {
        level = null;
        error = string.Empty;
        var found = false;
        var maximumLevel = 0;

        if (TryGetProperty(character, "jobLevels", out var jobLevels))
        {
            if (jobLevels.ValueKind != JsonValueKind.Object)
            {
                error = "jobLevels is not an object.";
                return false;
            }

            if (TryGetProperty(jobLevels, FisherJobId.ToString(CultureInfo.InvariantCulture), out var fisherProperty) ||
                TryGetProperty(jobLevels, "FSH", out fisherProperty))
            {
                if (!TryReadInt(fisherProperty, out var numericLevel))
                {
                    error = "Fisher jobLevels value is not an integer.";
                    return false;
                }

                maximumLevel = Math.Max(maximumLevel, numericLevel);
                found = true;
            }
        }

        if (TryGetProperty(character, "jobs", out var jobs))
        {
            if (jobs.ValueKind != JsonValueKind.Array)
            {
                error = "jobs is not an array.";
                return false;
            }

            foreach (var job in jobs.EnumerateArray())
            {
                if (job.ValueKind != JsonValueKind.Object)
                {
                    error = "jobs contains a non-object entry.";
                    return false;
                }

                var jobIdMatches = TryGetInt(job, "jobId", out var jobId) && jobId == FisherJobId;
                var abbrevMatches = string.Equals(GetString(job, "jobAbbrev"), "FSH", StringComparison.OrdinalIgnoreCase);
                if (!jobIdMatches && !abbrevMatches)
                    continue;

                if (!TryGetProperty(job, "level", out var jobLevelElement) ||
                    !TryReadInt(jobLevelElement, out var jobLevel))
                {
                    error = "Fisher jobs entry does not contain an integer level.";
                    return false;
                }

                maximumLevel = Math.Max(maximumLevel, jobLevel);
                found = true;
            }
        }

        level = found ? Math.Max(0, maximumLevel) : null;
        return true;
    }

    private static string GetString(JsonElement obj, string propertyName)
        => TryGetProperty(obj, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        return TryGetProperty(obj, propertyName, out var property) &&
               TryReadInt(property, out value);
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            _ => false,
        };
    }

    private static bool TryGetDateTimeOffset(
        JsonElement obj,
        string propertyName,
        out DateTimeOffset? value)
    {
        value = null;
        if (!TryGetProperty(obj, propertyName, out var property))
            return false;

        if (!TryReadDateTimeOffset(property, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    private static bool TryReadDateTimeOffset(JsonElement element, out DateTimeOffset value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(
                   element.GetString(),
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out value);
    }

    private static string ReadWarnings(JsonElement root, string fallback)
    {
        if (!TryGetProperty(root, "warnings", out var warnings) ||
            warnings.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var messages = warnings
            .EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        return messages.Length == 0 ? fallback : string.Join(" | ", messages!);
    }

    private static XaFishingRosterSnapshot Malformed(
        string detail,
        DateTimeOffset? generatedAtUtc = null)
        => new(
            XaFishingRosterReadStatus.MalformedResponse,
            generatedAtUtc,
            Array.Empty<XaFishingRosterEntry>(),
            detail);

    private static XaFishingRosterSnapshot UnsupportedContract(
        string detail,
        DateTimeOffset? generatedAtUtc = null)
        => new(
            XaFishingRosterReadStatus.UnsupportedContract,
            generatedAtUtc,
            Array.Empty<XaFishingRosterEntry>(),
            detail);

    private static bool TryGetProperty(JsonElement obj, string propertyName, out JsonElement property)
        => TryGetProperty(obj, out property, propertyName);

    private static bool TryGetProperty(
        JsonElement obj,
        out JsonElement property,
        params string[] propertyNames)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (obj.TryGetProperty(propertyName, out property))
                    return true;
            }
        }

        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in obj.EnumerateObject())
            {
                if (propertyNames.Any(propertyName =>
                        string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase)))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }
}

public enum XaFishingRosterReadStatus
{
    Ready,
    EmptyResponse,
    MalformedResponse,
    UnsupportedContract,
    FullRosterUnavailable,
    IpcFailure,
}

public sealed record XaFishingRosterEntry(
    string CharacterKey,
    int? FisherLevel,
    string Source,
    DateTimeOffset? SnapshotTimestamp);

public sealed record XaFishingRosterSnapshot(
    XaFishingRosterReadStatus Status,
    DateTimeOffset? GeneratedAtUtc,
    IReadOnlyList<XaFishingRosterEntry> Characters,
    string Detail)
{
    public bool IsUsable => Status == XaFishingRosterReadStatus.Ready;

    public static XaFishingRosterSnapshot Failure(
        XaFishingRosterReadStatus status,
        string detail)
        => new(status, null, Array.Empty<XaFishingRosterEntry>(), detail);
}

public enum FishingRelogPrepAction
{
    FinishVermaxionPostprocess,
    ReleaseVermaxionSuppression,
    DisableAutoRetainerMultiMode,
    SendCommand,
    Wait,
}

public sealed record FishingRelogPrepStep(FishingRelogPrepAction Action, string Command = "", int DelayMilliseconds = 0);

public static class FishingRelogPrepPolicy
{
    public static IReadOnlyList<FishingRelogPrepStep> BuildReleaseSequence(string characterKey)
    {
        var normalizedKey = characterKey.Trim();
        return
        [
            new(FishingRelogPrepAction.FinishVermaxionPostprocess),
            new(FishingRelogPrepAction.ReleaseVermaxionSuppression),
            new(FishingRelogPrepAction.DisableAutoRetainerMultiMode),
            new(FishingRelogPrepAction.SendCommand, $"/ays relog {normalizedKey}"),
        ];
    }
}

public static class FishingRelogDiagnostics
{
    public static string FormatCommand(FishingRelogPrepStep step)
        => $"[Fishing][Relog] Sending {step.Command}";
}

public static class BeforeArMultiModePolicy
{
    public static bool ShouldRunBeforeAr(bool readSucceeded, bool multiModeEnabled)
        => readSucceeded && multiModeEnabled;
}

public enum FishingRelogRuntimeAction
{
    Complete,
    Fail,
    Wait,
    SendRelog,
}

public readonly record struct FishingRelogRuntimeDecision(
    FishingRelogRuntimeAction Action,
    string Reason);

public static class FishingRelogCommandPolicy
{
    public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMinutes(4);

    public static FishingRelogRuntimeDecision Evaluate(
        DateTimeOffset nowUtc,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? lastRelogCommandAtUtc,
        bool registrationOpen,
        bool readyForRelog,
        string blockedReason,
        bool targetReached,
        bool observableProgress,
        bool wrongCharacterArrived,
        TimeSpan? retryInterval = null,
        TimeSpan? overallTimeout = null)
    {
        if (targetReached)
            return new(FishingRelogRuntimeAction.Complete, "Arrived on the target character.");

        if (!registrationOpen)
            return new(FishingRelogRuntimeAction.Fail, "Ocean Fishing registration closed before relog completed.");

        var cappedOverallTimeout = overallTimeout ?? DefaultOverallTimeout;
        if (startedAtUtc != default && nowUtc - startedAtUtc >= cappedOverallTimeout)
            return new(FishingRelogRuntimeAction.Fail, $"Relog did not reach the selected character within {cappedOverallTimeout.TotalSeconds:F0}s.");

        if (!readyForRelog)
            return new(FishingRelogRuntimeAction.Wait, string.IsNullOrWhiteSpace(blockedReason) ? "Waiting for relog readiness." : blockedReason);

        if (!lastRelogCommandAtUtc.HasValue)
            return new(FishingRelogRuntimeAction.SendRelog, "Relog command has not been sent.");

        var cappedRetryInterval = retryInterval ?? DefaultRetryInterval;
        if (wrongCharacterArrived)
        {
            return nowUtc - lastRelogCommandAtUtc.Value >= cappedRetryInterval
                ? new(FishingRelogRuntimeAction.SendRelog, $"An intermediate character arrived; retrying relog to the selected target after {cappedRetryInterval.TotalSeconds:F0}s.")
                : new(FishingRelogRuntimeAction.Wait, "An intermediate character arrived; waiting until the relog command can be retried.");
        }

        if (observableProgress)
            return new(FishingRelogRuntimeAction.Wait, "Relog transition was observed; waiting for target character.");

        return nowUtc - lastRelogCommandAtUtc.Value >= cappedRetryInterval
            ? new(FishingRelogRuntimeAction.SendRelog, $"No logout or area transition was observed within {cappedRetryInterval.TotalSeconds:F0}s; retrying relog.")
            : new(FishingRelogRuntimeAction.Wait, "Waiting for observable relog progress.");
    }
}

public enum OceanFishingQueueAction
{
    SwitchToFisher,
    PrepareSupplies,
    TravelToLimsa,
    MoveToRegistrar,
    WaitForRegistrationOpen,
    InteractWithRegistrar,
    WaitForQueueConfirmation,
    WaitForDeparture,
    MoveToFishingPosition,
    CastLine,
    CloseResult,
    ReturnAfterCompletion,
    Complete,
    FailRegistrationClosed,
}

public readonly record struct OceanFishingQueueSnapshot(
    int CurrentJobId,
    bool SuppliesPrepared,
    ushort TerritoryType,
    double RegistrarDistance,
    bool RegistrationWindowOpen,
    bool RegistrationWindowClosed,
    bool QueueConfirmed,
    bool DutyActive,
    double FishingPositionDistance,
    bool ResultAddonVisible,
    bool FishingComplete,
    bool ReturnCommandSent);

public static class OceanFishingQueuePolicy
{
    public const int FisherJobId = 18;
    public const ushort LimsaTerritoryType = 129;
    public const double RegistrarInteractDistance = 2.0;
    public const double BoatFishingPositionTolerance = 0.5;

    public static OceanFishingQueueAction Decide(OceanFishingQueueSnapshot snapshot)
    {
        if (snapshot.FishingComplete)
            return snapshot.ReturnCommandSent
                ? OceanFishingQueueAction.Complete
                : OceanFishingQueueAction.ReturnAfterCompletion;

        if (snapshot.ResultAddonVisible)
            return OceanFishingQueueAction.CloseResult;

        if (snapshot.DutyActive)
        {
            return snapshot.FishingPositionDistance <= BoatFishingPositionTolerance
                ? OceanFishingQueueAction.CastLine
                : OceanFishingQueueAction.MoveToFishingPosition;
        }

        if (snapshot.QueueConfirmed)
            return OceanFishingQueueAction.WaitForDeparture;

        if (snapshot.RegistrationWindowClosed)
            return OceanFishingQueueAction.FailRegistrationClosed;

        if (snapshot.CurrentJobId != FisherJobId)
            return OceanFishingQueueAction.SwitchToFisher;

        if (snapshot.TerritoryType != LimsaTerritoryType)
            return OceanFishingQueueAction.TravelToLimsa;

        if (!snapshot.SuppliesPrepared)
            return OceanFishingQueueAction.PrepareSupplies;

        if (snapshot.RegistrarDistance > RegistrarInteractDistance)
            return OceanFishingQueueAction.MoveToRegistrar;

        return snapshot.RegistrationWindowOpen
            ? OceanFishingQueueAction.InteractWithRegistrar
            : OceanFishingQueueAction.WaitForRegistrationOpen;
    }
}

public static class OceanFishingRegistrarPolicy
{
    public static Vector3 ResolveApproachPosition(Vector3 fallbackPosition, Vector3? liveObjectPosition)
        => liveObjectPosition ?? fallbackPosition;

    public static bool IsWithinInteractionRange(double distance)
        => distance <= OceanFishingQueuePolicy.RegistrarInteractDistance;
}

internal readonly record struct OceanFishingDockPreparationDecision(
    bool RepairNeeded,
    bool LureRestockNeeded)
{
    public bool RequiresDockNavigation => RepairNeeded || LureRestockNeeded;
}

internal static class OceanFishingDockPreparationPolicy
{
    public const ushort LimsaTerritoryType = 129;
    public const uint MerchantAndMenderDataId = 1005422;
    public const uint DryskthotaDataId = 1005421;
    public const uint ArcanistsGuildAethernetId = 43;
    public const uint VersatileLureItemId = 29717;
    public const double InteractDistance = 3.0;
    public static readonly Vector3 MerchantAndMenderPosition = new(-399.0f, 3.0f, 80.0f);
    public static readonly Vector3 DryskthotaPosition = new(-409.42f, 4.0f, 74.48f);

    public static OceanFishingDockPreparationDecision Evaluate(
        bool repairNeeded,
        int currentLureCount,
        int lureTarget)
        => new(
            repairNeeded,
            Math.Max(0, currentLureCount) < Math.Max(0, lureTarget));

    public static bool IsLimsaSettlementReady(
        uint territoryType,
        bool betweenAreas,
        bool playerAvailable)
        => territoryType == LimsaTerritoryType && !betweenAreas && playerAvailable;

    public static Vector3 ResolveMerchantApproachPosition(
        Vector3 fallbackPosition,
        Vector3? dataIdObjectPosition,
        Vector3? nameFallbackObjectPosition)
        => dataIdObjectPosition ?? nameFallbackObjectPosition ?? fallbackPosition;

    public static int RequiredPurchaseQuantity(int currentCount, int targetCount, int maximumQuantity = 99)
    {
        var remaining = Math.Max(0, targetCount) - Math.Max(0, currentCount);
        return remaining <= 0
            ? 0
            : Math.Clamp(remaining, 1, Math.Max(1, maximumQuantity));
    }

    public static bool CanContinueAfterRestockFailure(int finalLureCount)
        => finalLureCount > 0;
}

public enum OceanFishingRegistrationDecision
{
    ContinueDialogs,
    WaitForQueueRecognitionGrace,
    QueueConfirmed,
    RegistrationExpired,
    GenuineFailure,
}

public static class OceanFishingRegistrationPolicy
{
    public static readonly TimeSpan QueueRecognitionGracePeriod = TimeSpan.FromSeconds(60);

    public static OceanFishingRegistrationDecision Decide(
        bool queueConfirmed,
        bool embarkAccepted,
        DateTimeOffset nowUtc,
        DateTimeOffset registrationDeadlineUtc,
        bool genuineFailure)
    {
        if (queueConfirmed)
            return OceanFishingRegistrationDecision.QueueConfirmed;
        if (genuineFailure)
            return OceanFishingRegistrationDecision.GenuineFailure;
        if (nowUtc < registrationDeadlineUtc)
            return OceanFishingRegistrationDecision.ContinueDialogs;
        if (embarkAccepted &&
            nowUtc < registrationDeadlineUtc + QueueRecognitionGracePeriod)
        {
            return OceanFishingRegistrationDecision.WaitForQueueRecognitionGrace;
        }

        return OceanFishingRegistrationDecision.RegistrationExpired;
    }

    public static bool ShouldRetainRegistrationLeases(OceanFishingRegistrationDecision decision)
        => decision is OceanFishingRegistrationDecision.ContinueDialogs or
           OceanFishingRegistrationDecision.WaitForQueueRecognitionGrace or
           OceanFishingRegistrationDecision.QueueConfirmed;
}

public enum OceanFishingQueueEvidence
{
    None,
    InDutyQueue,
    WaitingForDuty,
    WaitingForDutyFinder,
    OceanFishingDutyEntry,
    ContentsFinderConfirm,
}

public static class OceanFishingQueueEvidencePolicy
{
    public static OceanFishingQueueEvidence Detect(
        bool inDutyQueue,
        bool waitingForDuty,
        bool waitingForDutyFinder,
        bool oceanFishingDutyActive,
        bool contentsFinderConfirmVisible)
    {
        if (oceanFishingDutyActive)
            return OceanFishingQueueEvidence.OceanFishingDutyEntry;
        if (contentsFinderConfirmVisible)
            return OceanFishingQueueEvidence.ContentsFinderConfirm;
        if (inDutyQueue)
            return OceanFishingQueueEvidence.InDutyQueue;
        if (waitingForDuty)
            return OceanFishingQueueEvidence.WaitingForDuty;
        if (waitingForDutyFinder)
            return OceanFishingQueueEvidence.WaitingForDutyFinder;
        return OceanFishingQueueEvidence.None;
    }
}

public static class OceanFishingDialoguePolicy
{
    public const string SheetName = "custom/006/CtsIkdEntrance_00663";
    public const uint BoardingRow = 4;
    public const uint EmbarkRow = 10;
    public const string EnglishEmbarkPrefix = "Embark to ";

    public static bool Matches(string actualText, string localizedSheetText)
    {
        var actual = actualText?.Trim() ?? string.Empty;
        var localized = localizedSheetText?.Trim() ?? string.Empty;
        return localized.Length > 0 &&
               (string.Equals(actual, localized, StringComparison.Ordinal) ||
                actual.Contains(localized, StringComparison.Ordinal));
    }

    public static bool MatchesEmbarkPrompt(string actualText, string localizedSheetText)
    {
        if (Matches(actualText, localizedSheetText))
            return true;

        var normalized = NormalizeWhitespace(actualText);
        if (!normalized.StartsWith(EnglishEmbarkPrefix, StringComparison.Ordinal) ||
            !normalized.EndsWith("?", StringComparison.Ordinal))
        {
            return false;
        }

        var destination = normalized[EnglishEmbarkPrefix.Length..^1].Trim();
        return destination.Length > 0;
    }

    public static string DescribeEmbarkExpectation(string localizedSheetText)
    {
        var localized = localizedSheetText?.Trim();
        if (string.IsNullOrWhiteSpace(localized))
            localized = "<unavailable>";

        return $"{SheetName} row {EmbarkRow} text '{localized}' or English route prompt '{EnglishEmbarkPrefix}...?'.";
    }

    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}

public static class OceanFishingCompletionPolicy
{
    public static bool ShouldInferFromDutyContextLoss(
        bool dutyContextPreviouslyObserved,
        bool stillInOceanFishingTerritory,
        bool playerAvailable,
        bool areaTransitioning)
        => dutyContextPreviouslyObserved &&
           !stillInOceanFishingTerritory &&
           playerAvailable &&
           !areaTransitioning;
}

public enum OceanFishingAttunementAction
{
    UseUnlockedShard,
    NavigateToLockedShard,
    AttuneLockedShard,
    VerifyAttunement,
    WalkDirect,
}

public static class OceanFishingAttunementPolicy
{
    public static readonly TimeSpan VerificationWait = TimeSpan.FromSeconds(10);

    public static OceanFishingAttunementAction Decide(
        bool unlocked,
        bool shardLoaded,
        bool inInteractionRange,
        bool attunementAttempted,
        TimeSpan sinceAttempt)
    {
        if (unlocked)
            return OceanFishingAttunementAction.UseUnlockedShard;
        if (!attunementAttempted)
            return shardLoaded && inInteractionRange
                ? OceanFishingAttunementAction.AttuneLockedShard
                : OceanFishingAttunementAction.NavigateToLockedShard;
        return sinceAttempt < VerificationWait
            ? OceanFishingAttunementAction.VerifyAttunement
            : OceanFishingAttunementAction.WalkDirect;
    }
}

public readonly record struct AutoRetainerMultiModeReadResult(bool Success, bool Enabled, string Error)
{
    public static AutoRetainerMultiModeReadResult Known(bool enabled)
        => new(true, enabled, string.Empty);

    public static AutoRetainerMultiModeReadResult Failed(string error)
        => new(false, false, error);
}

public readonly record struct PluginStateReadResult(bool Success, bool Enabled, string Error)
{
    public static PluginStateReadResult Known(bool enabled) => new(true, enabled, string.Empty);
    public static PluginStateReadResult Failed(string error) => new(false, false, error);
}

public readonly record struct PluginBusyReadResult(bool Success, bool Busy, string Error)
{
    public static PluginBusyReadResult Known(bool busy) => new(true, busy, string.Empty);
    public static PluginBusyReadResult Failed(string error) => new(false, false, error);
}

public static class FishingExternalStatePolicy
{
    public static bool ShouldRestore(bool? initialState, bool changedByVermaxion)
        => initialState.HasValue && changedByVermaxion;
}

public static class LifestreamCommandPolicy
{
    public static string NormalizeForIpc(string command)
    {
        var normalized = command.Trim();
        return normalized.StartsWith("/li ", StringComparison.OrdinalIgnoreCase)
            ? normalized[4..].Trim()
            : normalized;
    }
}

public enum FishingCastDecision
{
    Suppressed = 0,
    Attempt = 1,
    Acknowledged = 2,
}

public readonly record struct FishingCastEvaluation(
    FishingCastDecision Decision,
    string Gate);

public static class FishingCastPolicy
{
    public const string CastCommand = "/ahstart";
    public const string DirectCastFallbackCommand = "/ac cast";
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan CanFishFallbackDelay = TimeSpan.FromSeconds(10);

    public static FishingCastEvaluation Evaluate(
        bool enabled,
        bool inFishingContext,
        bool zoneTransitionActive,
        bool playerAvailable,
        bool gatheringConditionActive,
        bool fishingConditionActive,
        bool resultWindowVisible,
        TimeSpan sinceLastAttempt)
    {
        if (!enabled)
            return Suppressed("disabled");
        if (resultWindowVisible)
            return Suppressed("result window visible");
        if (!inFishingContext)
            return Suppressed("Ocean Fishing duty context inactive");
        if (zoneTransitionActive)
            return Suppressed("route transition active");
        if (gatheringConditionActive || fishingConditionActive)
            return new FishingCastEvaluation(FishingCastDecision.Acknowledged, "Fishing/Gathering active");
        if (!playerAvailable)
            return Suppressed("player unavailable");
        if (sinceLastAttempt < RetryInterval)
            return Suppressed("waiting for retry interval");

        return new FishingCastEvaluation(FishingCastDecision.Attempt, string.Empty);
    }

    private static FishingCastEvaluation Suppressed(string gate)
        => new(FishingCastDecision.Suppressed, gate);
}

public sealed record FishingRepairDecision(bool ShouldRepair, string AdsMode, string Reason);

public static class FishingRepairPolicy
{
    public static FishingRepairDecision Evaluate(
        FishingRepairMode mode,
        int thresholdPercent,
        bool durabilityKnown,
        int lowestDurabilityPercent)
    {
        if (mode == FishingRepairMode.Disabled || thresholdPercent <= 0)
            return new FishingRepairDecision(false, string.Empty, "Repair disabled.");

        var adsMode = ToAdsMode(mode);
        if (string.IsNullOrWhiteSpace(adsMode))
            return new FishingRepairDecision(false, string.Empty, "Repair mode is invalid.");

        if (!durabilityKnown)
            return new FishingRepairDecision(false, adsMode, "Durability unavailable.");

        var threshold = Math.Clamp(thresholdPercent, 0, 100);
        var durability = Math.Clamp(lowestDurabilityPercent, 0, 100);
        if (durability <= threshold)
            return new FishingRepairDecision(true, adsMode, $"Durability {durability}% is at or below threshold {threshold}%.");

        return new FishingRepairDecision(false, adsMode, $"Durability {durability}% is above threshold {threshold}%.");
    }

    public static string ToAdsMode(FishingRepairMode mode)
        => mode switch
        {
            FishingRepairMode.Self => "self",
            FishingRepairMode.NpcNoInn => "npc-no-inn",
            FishingRepairMode.NpcNoTeleportNoInn => "npc-no-teleport-no-inn",
            _ => string.Empty,
        };
}

public static class FishingOperationPolicy
{
    public static int ResolveLureRestockTarget(int configuredTarget)
        => configuredTarget > 0
            ? configuredTarget
            : FishingDefaults.LureRestockTarget;

    public static FishingRepairDecision EvaluateRepair(
        FishingOperationSettings settings,
        bool durabilityKnown,
        int lowestDurabilityPercent)
        => FishingRepairPolicy.Evaluate(
            settings.RepairMode,
            settings.RepairThresholdPercent,
            durabilityKnown,
            lowestDurabilityPercent);

    public static string ResolveReturnCommand(FishingOperationSettings settings)
    {
        if (settings.ReturnDestination == FishingReturnDestination.None)
            return string.Empty;

        var configuredCommand = settings.ReturnCommand?.Trim() ?? string.Empty;
        return settings.ReturnDestination switch
        {
            FishingReturnDestination.Home => string.IsNullOrWhiteSpace(configuredCommand)
                ? "/li home"
                : configuredCommand,
            FishingReturnDestination.Limsa => string.IsNullOrWhiteSpace(configuredCommand)
                ? "/li limsa"
                : configuredCommand,
            FishingReturnDestination.FreeCompany => string.IsNullOrWhiteSpace(configuredCommand)
                ? "/li fc"
                : configuredCommand,
            FishingReturnDestination.Inn => string.IsNullOrWhiteSpace(configuredCommand)
                ? "/li inn"
                : configuredCommand,
            FishingReturnDestination.Custom => configuredCommand,
            _ => string.Empty,
        };
    }
}
