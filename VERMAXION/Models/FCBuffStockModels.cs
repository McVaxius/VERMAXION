using System;
using System.Collections.Generic;

namespace VERMAXION.Models;

[Serializable]
public sealed class FcActionStockEntry
{
    public int KnownSealSweetenerTwoCount { get; set; }
    public DateTime LastVerifiedAtUtc { get; set; } = DateTime.MinValue;
}

public enum FcActionInventoryReadStatus
{
    Success,
    Failed,
}

public readonly record struct FcActionInventoryReadResult(
    FcActionInventoryReadStatus Status,
    int Count,
    string Failure)
{
    public static FcActionInventoryReadResult Succeeded(int count) =>
        new(FcActionInventoryReadStatus.Success, Math.Max(0, count), string.Empty);

    public static FcActionInventoryReadResult Failed(string failure) =>
        new(FcActionInventoryReadStatus.Failed, 0, failure);
}

public enum FcBuffStockAction
{
    Satisfied,
    ActivateCached,
    Reconcile,
    Purchase,
}

public static class FcBuffStockPolicy
{
    public static FcBuffStockAction Decide(
        bool sealSweetenerTwoAlreadyActive,
        FcActionStockEntry? cachedStock,
        bool activationFailed)
    {
        if (sealSweetenerTwoAlreadyActive)
            return FcBuffStockAction.Satisfied;
        if (activationFailed)
            return FcBuffStockAction.Reconcile;
        return cachedStock is { KnownSealSweetenerTwoCount: > 0 }
            ? FcBuffStockAction.ActivateCached
            : FcBuffStockAction.Reconcile;
    }

    public static bool ApplyReconciliation(
        IDictionary<ulong, FcActionStockEntry> ledger,
        ulong freeCompanyId,
        FcActionInventoryReadResult result,
        DateTime verifiedAtUtc)
    {
        if (freeCompanyId == 0 || result.Status != FcActionInventoryReadStatus.Success)
            return false;

        ledger[freeCompanyId] = new FcActionStockEntry
        {
            KnownSealSweetenerTwoCount = Math.Max(0, result.Count),
            LastVerifiedAtUtc = verifiedAtUtc.ToUniversalTime(),
        };
        return true;
    }

    public static bool ApplyConfirmedActivation(
        IDictionary<ulong, FcActionStockEntry> ledger,
        ulong freeCompanyId,
        DateTime verifiedAtUtc)
    {
        if (freeCompanyId == 0 ||
            !ledger.TryGetValue(freeCompanyId, out var entry) ||
            entry.KnownSealSweetenerTwoCount <= 0)
        {
            return false;
        }

        entry.KnownSealSweetenerTwoCount--;
        entry.LastVerifiedAtUtc = verifiedAtUtc.ToUniversalTime();
        return true;
    }
}
