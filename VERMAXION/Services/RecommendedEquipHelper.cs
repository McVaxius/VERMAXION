using System;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Thin native adapter for RecommendEquipModule. It never subscribes to framework
/// events: callers poll it from their existing bounded state machine.
/// </summary>
internal sealed unsafe class RecommendedEquipHelper
{
    private bool operationActive;
    private bool equipIssued;
    private DateTime setupStartedAt;
    private DateTime nextPollAt;
    private DateTime equipIssuedAt;

    internal bool TryBegin(uint classJobId, out string error)
    {
        Cancel();
        try
        {
            var module = RecommendEquipModule.Instance();
            if (module == null)
            {
                error = "RecommendEquipModule is unavailable.";
                return false;
            }

            if (classJobId is 0 or > byte.MaxValue)
            {
                error = $"Class/job {classJobId} cannot be passed to RecommendEquipModule.";
                module->Clear();
                return false;
            }

            // The native Boolean is unreliable. IsUpdating is the authoritative
            // completion signal used by Questionable Companion.
            module->SetupForClassJob((byte)classJobId);
            operationActive = true;
            equipIssued = false;
            setupStartedAt = DateTime.UtcNow;
            nextPollAt = setupStartedAt;
            equipIssuedAt = DateTime.MinValue;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Cancel();
            return false;
        }
    }

    internal RecommendedEquipmentProgress Poll(out string error)
    {
        if (!operationActive)
        {
            error = "No recommended-equipment operation is active.";
            return RecommendedEquipmentProgress.Failed;
        }

        try
        {
            var module = RecommendEquipModule.Instance();
            if (module == null)
            {
                error = "RecommendEquipModule became unavailable.";
                Cancel();
                return RecommendedEquipmentProgress.Failed;
            }

            var now = DateTime.UtcNow;
            if (equipIssued)
            {
                if (now - equipIssuedAt < TimeSpan.FromSeconds(1))
                {
                    error = string.Empty;
                    return RecommendedEquipmentProgress.Pending;
                }

                module->Clear();
                operationActive = false;
                error = string.Empty;
                return RecommendedEquipmentProgress.Complete;
            }

            if (now < nextPollAt)
            {
                error = string.Empty;
                return RecommendedEquipmentProgress.Pending;
            }
            nextPollAt = now + TimeSpan.FromMilliseconds(100);

            if (module->IsUpdating)
            {
                if (now - setupStartedAt < TimeSpan.FromSeconds(10))
                {
                    error = string.Empty;
                    return RecommendedEquipmentProgress.Pending;
                }

                error = "RecommendEquipModule did not finish calculating within 10 seconds.";
                Cancel();
                return RecommendedEquipmentProgress.Failed;
            }

            module->EquipRecommendedGear();
            equipIssued = true;
            equipIssuedAt = now;
            error = string.Empty;
            return RecommendedEquipmentProgress.Pending;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Cancel();
            return RecommendedEquipmentProgress.Failed;
        }
    }

    internal void Cancel()
    {
        try
        {
            var module = RecommendEquipModule.Instance();
            if (operationActive && module != null)
                module->Clear();
        }
        catch
        {
            // Cleanup is best-effort and must never leak into engine cancellation.
        }
        finally
        {
            operationActive = false;
            equipIssued = false;
            setupStartedAt = DateTime.MinValue;
            nextPollAt = DateTime.MinValue;
            equipIssuedAt = DateTime.MinValue;
        }
    }
}
