using System;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace VERMAXION.IPC;

public sealed class AdsUtilityStatusSnapshot
{
    public static AdsUtilityStatusSnapshot Empty { get; } = new();

    public bool IsAvailable { get; init; }
    public bool StatusReadable { get; init; }
    public bool UtilityRunning { get; init; }
    public string UtilityTask { get; init; } = string.Empty;
    public string UtilityMode { get; init; } = string.Empty;
    public string UtilityStatus { get; init; } = string.Empty;
    public string UtilityLastSuccess { get; init; } = string.Empty;
    public string UtilityLastFailure { get; init; } = string.Empty;
    public DateTime? UtilityCompletedAtUtc { get; init; }
    public DateTime CapturedAtUtc { get; init; }
}

public sealed class AdsShopPurchaseStatusSnapshot
{
    public static AdsShopPurchaseStatusSnapshot Empty { get; } = new();

    public bool IsAvailable { get; init; }
    public bool StatusReadable { get; init; }
    public bool Running { get; init; }
    public bool Done { get; init; }
    public bool? Succeeded { get; init; }
    public string Phase { get; init; } = string.Empty;
    public uint ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int RequestedQuantity { get; init; }
    public int AcquiredQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public string FailureCode { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
    public string SuccessMessage { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
    public string LastStartError { get; init; } = string.Empty;
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime CapturedAtUtc { get; init; }
    public bool IsTerminal => Done && !Running;
}

public sealed class AdsIpcClient
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, bool> startRepairSubscriber;
    private readonly ICallGateSubscriber<string> getStatusJsonSubscriber;
    private readonly ICallGateSubscriber<uint, int, bool> startShopPurchaseSubscriber;
    private readonly ICallGateSubscriber<string> getShopPurchaseStatusJsonSubscriber;
    private readonly ICallGateSubscriber<bool> cancelUtilitySubscriber;
    private DateTime lastRefreshUtc = DateTime.MinValue;
    private DateTime lastShopRefreshUtc = DateTime.MinValue;

    public AdsUtilityStatusSnapshot Current { get; private set; } = AdsUtilityStatusSnapshot.Empty;
    public AdsShopPurchaseStatusSnapshot CurrentShopPurchase { get; private set; } =
        AdsShopPurchaseStatusSnapshot.Empty;

    public AdsIpcClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        startRepairSubscriber = pluginInterface.GetIpcSubscriber<string, bool>("ADS.StartRepair");
        getStatusJsonSubscriber = pluginInterface.GetIpcSubscriber<string>("ADS.GetStatusJson");
        startShopPurchaseSubscriber =
            pluginInterface.GetIpcSubscriber<uint, int, bool>("ADS.StartShopPurchase");
        getShopPurchaseStatusJsonSubscriber =
            pluginInterface.GetIpcSubscriber<string>("ADS.GetShopPurchaseStatusJson");
        cancelUtilitySubscriber = pluginInterface.GetIpcSubscriber<bool>("ADS.CancelUtility");
    }

    public bool StartShopPurchase(uint itemId, int quantity, out string failure)
    {
        failure = string.Empty;
        if (itemId == 0 || quantity < 1)
        {
            failure = "ADS shop purchases require a valid item and a positive quantity.";
            return false;
        }

        try
        {
            if (startShopPurchaseSubscriber.InvokeFunc(itemId, quantity))
            {
                CurrentShopPurchase = AdsShopPurchaseStatusSnapshot.Empty;
                lastShopRefreshUtc = DateTime.MinValue;
                return true;
            }

            var status = RefreshShopPurchase(force: true);
            failure = !string.IsNullOrWhiteSpace(status.LastStartError)
                ? status.LastStartError
                : !string.IsNullOrWhiteSpace(status.FailureMessage)
                    ? status.FailureMessage
                    : "ADS did not accept the shop purchase request.";
            return false;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            log.Warning($"[ADS] Failed to start shop purchase item={itemId}, quantity={quantity}: {ex.Message}");
            return false;
        }
    }

    public AdsShopPurchaseStatusSnapshot RefreshShopPurchase(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - lastShopRefreshUtc < TimeSpan.FromMilliseconds(250))
            return CurrentShopPurchase;

        lastShopRefreshUtc = now;
        try
        {
            var json = getShopPurchaseStatusJsonSubscriber.InvokeFunc();
            if (string.IsNullOrWhiteSpace(json))
            {
                CurrentShopPurchase = new AdsShopPurchaseStatusSnapshot
                {
                    IsAvailable = true,
                    StatusReadable = false,
                    CapturedAtUtc = now,
                };
                return CurrentShopPurchase;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            CurrentShopPurchase = new AdsShopPurchaseStatusSnapshot
            {
                IsAvailable = true,
                StatusReadable = true,
                Running = GetBool(root, "running"),
                Done = GetBool(root, "done"),
                Succeeded = GetNullableBool(root, "succeeded"),
                Phase = GetString(root, "phase"),
                ItemId = (uint)Math.Max(0, GetInt(root, "itemId")),
                ItemName = GetString(root, "itemName"),
                RequestedQuantity = GetInt(root, "requestedQuantity"),
                AcquiredQuantity = GetInt(root, "acquiredQuantity"),
                RemainingQuantity = GetInt(root, "remainingQuantity"),
                FailureCode = GetString(root, "failureCode"),
                StatusMessage = GetString(root, "statusMessage"),
                SuccessMessage = GetString(root, "successMessage"),
                FailureMessage = GetString(root, "failureMessage"),
                LastStartError = GetString(root, "lastStartError"),
                CompletedAtUtc = GetDateTime(root, "completedAtUtc"),
                CapturedAtUtc = now,
            };
            return CurrentShopPurchase;
        }
        catch (Exception ex)
        {
            log.Debug($"[ADS] Failed to read shop purchase status JSON: {ex.Message}");
            CurrentShopPurchase = new AdsShopPurchaseStatusSnapshot
            {
                IsAvailable = false,
                StatusReadable = false,
                CapturedAtUtc = now,
            };
            return CurrentShopPurchase;
        }
    }

    public bool CancelUtility(out string failure)
    {
        failure = string.Empty;
        try
        {
            if (cancelUtilitySubscriber.InvokeFunc())
                return true;
            failure = "ADS reported that no utility request was cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            log.Warning($"[ADS] Failed to cancel utility: {ex.Message}");
            return false;
        }
    }

    public bool StartRepair(string mode, out string failure)
    {
        failure = string.Empty;
        try
        {
            if (startRepairSubscriber.InvokeFunc(mode))
                return true;

            failure = BuildFailureText(Refresh(force: true));
            return false;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            log.Warning($"[ADS] Failed to start repair mode '{mode}': {ex.Message}");
            return false;
        }
    }

    public AdsUtilityStatusSnapshot Refresh(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - lastRefreshUtc < TimeSpan.FromMilliseconds(250))
            return Current;

        lastRefreshUtc = now;
        try
        {
            var json = getStatusJsonSubscriber.InvokeFunc();
            if (string.IsNullOrWhiteSpace(json))
            {
                Current = new AdsUtilityStatusSnapshot
                {
                    IsAvailable = true,
                    StatusReadable = false,
                    CapturedAtUtc = now,
                };
                return Current;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Current = new AdsUtilityStatusSnapshot
            {
                IsAvailable = true,
                StatusReadable = true,
                UtilityRunning = GetBool(root, "utilityRunning"),
                UtilityTask = GetString(root, "utilityTask"),
                UtilityMode = GetString(root, "utilityMode"),
                UtilityStatus = GetString(root, "utilityStatus"),
                UtilityLastSuccess = GetString(root, "utilityLastSuccess"),
                UtilityLastFailure = GetString(root, "utilityLastFailure"),
                UtilityCompletedAtUtc = GetDateTime(root, "utilityCompletedAtUtc"),
                CapturedAtUtc = now,
            };
            return Current;
        }
        catch (Exception ex)
        {
            log.Debug($"[ADS] Failed to read ADS status JSON: {ex.Message}");
            Current = new AdsUtilityStatusSnapshot
            {
                IsAvailable = false,
                StatusReadable = false,
                CapturedAtUtc = now,
            };
            return Current;
        }
    }

    private static string BuildFailureText(AdsUtilityStatusSnapshot status)
    {
        if (!string.IsNullOrWhiteSpace(status.UtilityLastFailure))
            return status.UtilityLastFailure;

        if (!string.IsNullOrWhiteSpace(status.UtilityStatus))
            return status.UtilityStatus;

        return status.StatusReadable
            ? "ADS did not accept the repair request."
            : "ADS status was not readable.";
    }

    private static string GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property)
           && property.ValueKind is JsonValueKind.True or JsonValueKind.False
           && property.GetBoolean();

    private static bool? GetNullableBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int GetInt(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.Number &&
           property.TryGetInt32(out var value)
            ? value
            : 0;

    private static DateTime? GetDateTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }
}
