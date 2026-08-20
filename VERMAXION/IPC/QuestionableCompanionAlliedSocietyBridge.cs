using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using ECommons.Reflection;

namespace VERMAXION.IPC;

public sealed class QuestionableCompanionAlliedSocietyBridge
{
    private const string PluginInternalName = "QSTCompanion";
    private const string ServicePropertyName = "AlliedSocietyRotationService";
    private const string ServiceTypeName = "QuestionableCompanion.Services.AlliedSocietyRotationService";
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private object? ownedService;
    private PropertyInfo? activeProperty;
    private PropertyInfo? phaseProperty;
    private MethodInfo? stopMethod;
    private DateTime nextOwnedShapeCheckAt;

    public bool OwnsRun => ownedService != null;

    public bool TryStart(string currentCharacterKey, out string error)
    {
        if (ownedService != null)
        {
            error = "A previous VerMAXION-owned Allied Society run has not been released.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(currentCharacterKey))
        {
            error = "Current character key is empty.";
            return false;
        }

        try
        {
            if (!TryResolveService(out var service, out var startMethod, out error))
                return false;
            if (!TryReadActive(service, out var active, out _, out error))
                return false;
            if (active)
            {
                error = "Questionable Companion Allied Society rotation is already active.";
                return false;
            }

            ownedService = service;
            nextOwnedShapeCheckAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
            startMethod.Invoke(service, [new List<string> { currentCharacterKey }]);
            if (!TryReadActive(service, out active, out _, out error) || !active)
            {
                error = string.IsNullOrWhiteSpace(error)
                    ? "StartRotation returned without activating the Allied Society service."
                    : error;
                if (!TryStopOwned(out var stopError) && !string.IsNullOrWhiteSpace(stopError))
                    error = $"{error} Cleanup StopRotation failed: {stopError}";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = DescribeException(ex);
            if (ownedService != null && !TryStopOwned(out var stopError) && !string.IsNullOrWhiteSpace(stopError))
                error = $"{error} Cleanup StopRotation failed: {stopError}";
            return false;
        }
    }

    public bool TryReadOwnedState(out bool active, out string phase, out string error)
    {
        if (ownedService == null)
        {
            active = false;
            phase = string.Empty;
            error = "VerMAXION does not own an Allied Society rotation.";
            return false;
        }

        if (!TryReadActive(ownedService, out active, out phase, out error))
            return false;

        if ((!active || DateTime.UtcNow >= nextOwnedShapeCheckAt) && !IsOwnedServiceStillLoaded(out error))
        {
            active = false;
            phase = string.Empty;
            return false;
        }

        return true;
    }

    public bool TryStopOwned(out string error)
    {
        if (ownedService == null)
        {
            error = string.Empty;
            return true;
        }

        try
        {
            stopMethod!.Invoke(ownedService, null);
            ReleaseOwnership();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = DescribeException(ex);
            return false;
        }
    }

    public void ReleaseOwnership()
    {
        ownedService = null;
        activeProperty = null;
        phaseProperty = null;
        stopMethod = null;
        nextOwnedShapeCheckAt = DateTime.MinValue;
    }

    private bool IsOwnedServiceStillLoaded(out string error)
    {
        nextOwnedShapeCheckAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin(
                    PluginInternalName,
                    out object plugin,
                    out AssemblyLoadContext? _,
                    true,
                    true) || plugin == null)
            {
                error = "Questionable Companion unloaded during the owned Allied Society run.";
                return false;
            }

            var service = plugin.GetType().GetProperty(ServicePropertyName, InstanceMembers)?.GetValue(plugin);
            if (!ReferenceEquals(service, ownedService))
            {
                error = "Questionable Companion AlliedSocietyRotationService changed during the owned run.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = DescribeException(ex);
            return false;
        }
    }

    private bool TryResolveService(out object service, out MethodInfo startMethod, out string error)
    {
        service = null!;
        startMethod = null!;
        if (!DalamudReflector.TryGetDalamudPlugin(
                PluginInternalName,
                out object plugin,
                out AssemblyLoadContext? _,
                true,
                true) || plugin == null)
        {
            error = "Questionable Companion is not loaded.";
            return false;
        }

        var property = plugin.GetType().GetProperty(ServicePropertyName, InstanceMembers);
        service = property?.GetValue(plugin) ?? null!;
        if (service == null || !string.Equals(service.GetType().FullName, ServiceTypeName, StringComparison.Ordinal))
        {
            error = "Questionable Companion AlliedSocietyRotationService shape is unavailable.";
            return false;
        }

        activeProperty = service.GetType().GetProperty("IsRotationActive", BindingFlags.Instance | BindingFlags.Public);
        phaseProperty = service.GetType().GetProperty("CurrentPhase", BindingFlags.Instance | BindingFlags.Public);
        startMethod = service.GetType().GetMethod(
            "StartRotation",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(List<string>)],
            modifiers: null)!;
        stopMethod = service.GetType().GetMethod(
            "StopRotation",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (activeProperty?.PropertyType != typeof(bool) || phaseProperty == null || startMethod == null || stopMethod == null)
        {
            error = "Questionable Companion Allied Society public contract is incomplete.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryReadActive(object service, out bool active, out string phase, out string error)
    {
        active = false;
        phase = string.Empty;
        try
        {
            if (activeProperty?.GetValue(service) is not bool value)
            {
                error = "Allied Society IsRotationActive was not readable.";
                return false;
            }

            active = value;
            phase = phaseProperty?.GetValue(service)?.ToString() ?? "Unknown";
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = DescribeException(ex);
            return false;
        }
    }

    private static string DescribeException(Exception exception)
    {
        var current = exception;
        while (current is TargetInvocationException { InnerException: not null })
            current = current.InnerException;
        return $"{current.GetType().Name}: {current.Message}";
    }
}
