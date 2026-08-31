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
    public static int RequiredPurchaseQuantity(
        bool maintainTarget,
        int configuredQuantity,
        int liveCount,
        bool willActivate)
    {
        var quantityOrTarget = FCBuffRecoveryPolicy.ClampPurchaseAttempts(configuredQuantity);
        var stock = Math.Max(0, liveCount);
        if (!maintainTarget)
            return stock == 0 ? quantityOrTarget : 0;

        return Math.Max(0, quantityOrTarget + (willActivate ? 1 : 0) - stock);
    }

    public static FcBuffStockAction Decide(
        bool allowActivation,
        bool sealSweetenerTwoAlreadyActive,
        FcActionStockEntry? stock,
        bool reconciliationRequired)
    {
        if (allowActivation && sealSweetenerTwoAlreadyActive)
            return FcBuffStockAction.Satisfied;
        if (reconciliationRequired || stock == null)
            return FcBuffStockAction.Reconcile;
        if (stock.KnownSealSweetenerTwoCount <= 0)
            return FcBuffStockAction.Purchase;
        return allowActivation
            ? FcBuffStockAction.ActivateCached
            : FcBuffStockAction.Satisfied;
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
