using System;
using System.IO;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class LifecycleHardeningTests
{
    [Fact]
    public void LoggedOutFinalHandoffRetainsEveryNonSessionBlocker()
    {
        var quiet = new ExternalHandoffSnapshot(0, false, false, false, false, false, false, false, false, false, false);
        var blocked = new[]
        {
            quiet with { IKDResultVisible = true },
            quiet with { IKDResultVisible = true, IKDResultReady = true },
            quiet with { TerritoryType = 900 },
            quiet with { TerritoryType = 1163 },
            quiet with { LifestreamBusy = true },
            quiet with { BoundByDuty = true },
            quiet with { DutyQueueActive = true },
            quiet with { AreaTransitionActive = true },
            quiet with { OccupiedOrCutscene = true },
            quiet with { CombatOrCasting = true },
        };
        Assert.All(blocked, snapshot => Assert.NotNull(ExternalHandoffPolicy.GetBlocker(snapshot, requireActiveSession: false)));
    }

    [Fact]
    public void LoggedOutFinalHandoffCanIgnoreOnlySessionGates()
    {
        var snapshot = new ExternalHandoffSnapshot(
            TerritoryType: 0,
            IKDResultVisible: false,
            IKDResultReady: false,
            LifestreamBusy: false,
            BoundByDuty: false,
            DutyQueueActive: false,
            AreaTransitionActive: false,
            OccupiedOrCutscene: false,
            CombatOrCasting: false,
            LoggedIn: false,
            PlayerAvailable: false);

        Assert.Equal("client is not logged in", ExternalHandoffPolicy.GetBlocker(snapshot));
        Assert.Null(ExternalHandoffPolicy.GetBlocker(snapshot, requireActiveSession: false));
    }

    [Fact]
    public void OwnedLoggedOutFinalHandoffUsesRelaxedSessionGate()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var engine = File.ReadAllText(Path.Combine(root, "VERMAXION", "Services", "VermaxionEngine.cs"));
        var start = engine.IndexOf("private string? GetFinalHandoffBlocker()", StringComparison.Ordinal);
        var end = engine.IndexOf("private string? GetServiceOwnedHandoffBlocker()", start, StringComparison.Ordinal);
        var method = engine[start..end];

        Assert.DoesNotContain("return GetHandoffBlocker();", method, StringComparison.Ordinal);
        Assert.Contains("GetServiceOwnedHandoffBlocker()", method, StringComparison.Ordinal);
        Assert.Contains(
            "GetExternalHandoffBlocker(requireActiveSession: fishingService.IsActive)",
            method,
            StringComparison.Ordinal);
    }
}
