using System;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class AfterArParkService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PlayerSettleTime = TimeSpan.FromSeconds(2);

    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly LifestreamIPC lifestream;
    private DateTime startedAt;
    private DateTime settledSince;
    private bool commandIssued;

    public bool IsActive { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public string StatusText { get; private set; } = "Idle";

    public AfterArParkService(IPluginLog log, IClientState clientState, LifestreamIPC lifestream)
    {
        this.log = log;
        this.clientState = clientState;
        this.lifestream = lifestream;
    }

    public void Start(CharacterConfig config)
    {
        Reset();
        if (!TryResolveCommand(config.AfterArParkDestination, config.AfterArParkCustomCommand, out var command, out var error))
        {
            Fail(error);
            return;
        }

        startedAt = DateTime.UtcNow;
        if (!lifestream.ExecuteCommand(command))
        {
            Fail($"Lifestream rejected parking command {command}.");
            return;
        }

        commandIssued = true;
        IsActive = true;
        StatusText = $"Waiting for {command} route and player settlement.";
        log.Information($"[AfterArPark] Issued owned route once: {command}");
    }

    public void Update()
    {
        if (!IsActive || !commandIssued)
            return;

        var now = DateTime.UtcNow;
        if (now - startedAt >= Timeout)
        {
            Fail("After-AR parking timed out after three minutes; the route will not be retried.");
            return;
        }

        if (lifestream.IsBusy() || !clientState.IsLoggedIn || !GameHelpers.IsPlayerAvailable())
        {
            settledSince = DateTime.MinValue;
            StatusText = "Waiting for Lifestream idle and an available player.";
            return;
        }

        if (settledSince == DateTime.MinValue)
        {
            settledSince = now;
            StatusText = "Player available; settling parking destination.";
            return;
        }

        if (now - settledSince < PlayerSettleTime)
            return;

        IsActive = false;
        IsComplete = true;
        StatusText = "After-AR parking complete.";
    }

    public void Cancel(string reason = "After-AR parking cancelled")
    {
        if (!IsActive)
            return;
        IsActive = false;
        IsFailed = true;
        StatusText = reason;
    }

    public void Reset()
    {
        startedAt = DateTime.MinValue;
        settledSince = DateTime.MinValue;
        commandIssued = false;
        IsActive = false;
        IsComplete = false;
        IsFailed = false;
        StatusText = "Idle";
    }

    public static bool TryResolveCommand(
        AfterArParkDestination destination,
        string customCommand,
        out string command,
        out string error)
    {
        command = destination switch
        {
            AfterArParkDestination.Home => "/li home",
            AfterArParkDestination.Limsa => "/li limsa",
            AfterArParkDestination.FreeCompany => "/li fc",
            AfterArParkDestination.Inn => "/li inn",
            AfterArParkDestination.Workshop => "/li ws",
            AfterArParkDestination.Custom => (customCommand ?? string.Empty).Trim(),
            _ => string.Empty,
        };

        if (destination == AfterArParkDestination.Custom &&
            (!command.StartsWith("/li ", StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(command[4..]) ||
             command.Contains('\r') || command.Contains('\n')))
        {
            error = "Custom parking command must be one non-empty /li ... command.";
            command = string.Empty;
            return false;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            error = "Parking destination is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void Fail(string error)
    {
        IsActive = false;
        IsComplete = false;
        IsFailed = true;
        StatusText = error;
        log.Warning($"[AfterArPark] {error}");
    }
}
