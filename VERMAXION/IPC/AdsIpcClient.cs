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

public sealed class AdsIpcClient
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, bool> startRepairSubscriber;
    private readonly ICallGateSubscriber<string> getStatusJsonSubscriber;
    private DateTime lastRefreshUtc = DateTime.MinValue;

    public AdsUtilityStatusSnapshot Current { get; private set; } = AdsUtilityStatusSnapshot.Empty;

    public AdsIpcClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        startRepairSubscriber = pluginInterface.GetIpcSubscriber<string, bool>("ADS.StartRepair");
        getStatusJsonSubscriber = pluginInterface.GetIpcSubscriber<string>("ADS.GetStatusJson");
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

    private static DateTime? GetDateTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }
}
