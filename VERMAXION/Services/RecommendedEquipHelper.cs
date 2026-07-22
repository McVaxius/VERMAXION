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

            if (classJobId is 0 or > byte.MaxValue || !module->SetupForClassJob((byte)classJobId))
            {
                error = $"RecommendEquipModule rejected class/job {classJobId}.";
                module->Clear();
                return false;
            }

            operationActive = true;
            equipIssued = false;
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

            if (module->IsUpdating || module->EquippedMainHand == null)
            {
                error = string.Empty;
                return RecommendedEquipmentProgress.Pending;
            }

            if (!equipIssued)
            {
                module->EquipRecommendedGear();
                equipIssued = true;
            }

            module->Clear();
            operationActive = false;
            error = string.Empty;
            return RecommendedEquipmentProgress.Complete;
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
        }
    }
}
