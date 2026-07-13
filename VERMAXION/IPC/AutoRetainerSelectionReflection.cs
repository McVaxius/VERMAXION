using System;
using System.Collections;
using System.Reflection;
using VERMAXION.Models;

namespace VERMAXION.IPC;

internal static class AutoRetainerSelectionReflection
{
    private const string AutoRetainerTypeName = "AutoRetainer.AutoRetainer";
    private const string OfflineDataManagerTypeName = "AutoRetainer.Modules.OfflineDataManager";
    private const string ConfigMemberName = "C";
    private const string OfflineDataMemberName = "OfflineData";
    private const string ContentIdMemberName = "CID";
    private const string EnabledMemberName = "Enabled";

    private const BindingFlags StaticMembers =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static AutoRetainerSelectionReadResult Read(object autoRetainerPlugin, ulong localContentId)
    {
        if (localContentId == 0)
            return AutoRetainerSelectionReadResult.Failed("The local content ID was zero.");

        try
        {
            if (!TryResolveCurrentCharacterData(
                    autoRetainerPlugin,
                    localContentId,
                    out var characterData,
                    out var error))
            {
                return AutoRetainerSelectionReadResult.Failed(error);
            }

            return TryReadBooleanMember(characterData, EnabledMemberName, out var enabled, out error)
                ? AutoRetainerSelectionReadResult.Known(enabled)
                : AutoRetainerSelectionReadResult.Failed(error);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionReadResult.Failed(DescribeException(ex));
        }
    }

    public static AutoRetainerSelectionWriteResult Write(
        object autoRetainerPlugin,
        ulong localContentId,
        bool enabled)
    {
        if (localContentId == 0)
            return AutoRetainerSelectionWriteResult.Failed("The local content ID was zero.");

        try
        {
            if (!TryResolveCurrentCharacterData(
                    autoRetainerPlugin,
                    localContentId,
                    out var characterData,
                    out var error))
            {
                return AutoRetainerSelectionWriteResult.Failed(error);
            }

            if (!TryReadBooleanMember(characterData, EnabledMemberName, out var originalEnabled, out error))
                return AutoRetainerSelectionWriteResult.Failed(error);

            if (!TryValidateWritableBooleanMember(characterData, EnabledMemberName, out error))
                return AutoRetainerSelectionWriteResult.Failed(error, originalEnabled);

            if (!TryResolveSaveMethod(autoRetainerPlugin, out var saveMethod, out error))
                return AutoRetainerSelectionWriteResult.Failed(error, originalEnabled);

            if (!TryWriteBooleanMember(characterData, EnabledMemberName, enabled, out error))
                return AutoRetainerSelectionWriteResult.Failed(error, originalEnabled);

            if (!TryReadBooleanMember(characterData, EnabledMemberName, out var verifiedEnabled, out error))
                return AutoRetainerSelectionWriteResult.Failed(error);

            if (verifiedEnabled != enabled)
            {
                return AutoRetainerSelectionWriteResult.Failed(
                    $"{EnabledMemberName} did not retain the requested value {enabled}.",
                    verifiedEnabled);
            }

            try
            {
                saveMethod.Invoke(null, [false, true]);
            }
            catch (Exception ex)
            {
                return AutoRetainerSelectionWriteResult.Failed(
                    $"OfflineDataManager.WriteOfflineData(false, true) failed: {DescribeException(ex)}",
                    verifiedEnabled,
                    saveInvoked: true);
            }

            var finalRead = Read(autoRetainerPlugin, localContentId);
            if (!finalRead.Success)
            {
                return AutoRetainerSelectionWriteResult.Failed(
                    $"The selection could not be verified after persistence: {finalRead.Error}",
                    verifiedEnabled,
                    saveInvoked: true);
            }

            if (finalRead.Enabled != enabled)
            {
                return AutoRetainerSelectionWriteResult.Failed(
                    $"The persisted selection did not retain the requested value {enabled}.",
                    finalRead.Enabled,
                    saveInvoked: true);
            }

            return AutoRetainerSelectionWriteResult.Verified(finalRead.Enabled);
        }
        catch (Exception ex)
        {
            return AutoRetainerSelectionWriteResult.Failed(DescribeException(ex));
        }
    }

    private static bool TryResolveSaveMethod(
        object autoRetainerPlugin,
        out MethodInfo saveMethod,
        out string error)
    {
        saveMethod = null!;
        error = string.Empty;

        var assembly = autoRetainerPlugin.GetType().Assembly;
        var managerType = assembly.GetType(OfflineDataManagerTypeName, throwOnError: false, ignoreCase: false);
        if (managerType == null)
        {
            error = $"{OfflineDataManagerTypeName} was not available.";
            return false;
        }

        var resolvedMethod = managerType.GetMethod(
            "WriteOfflineData",
            StaticMembers,
            binder: null,
            types: [typeof(bool), typeof(bool)],
            modifiers: null);
        if (resolvedMethod == null)
        {
            error = "OfflineDataManager.WriteOfflineData(bool, bool) was not available.";
            return false;
        }

        saveMethod = resolvedMethod;
        return true;
    }

    private static bool TryResolveCurrentCharacterData(
        object autoRetainerPlugin,
        ulong localContentId,
        out object characterData,
        out string error)
    {
        characterData = null!;
        error = string.Empty;

        var assembly = autoRetainerPlugin.GetType().Assembly;
        var autoRetainerType = assembly.GetType(AutoRetainerTypeName, throwOnError: false, ignoreCase: false);
        if (autoRetainerType == null)
        {
            error = $"{AutoRetainerTypeName} was not available.";
            return false;
        }

        if (!TryGetMemberValue(autoRetainerType, null, ConfigMemberName, StaticMembers, out var config, out error) ||
            config == null)
        {
            error = string.IsNullOrWhiteSpace(error)
                ? $"{AutoRetainerTypeName}.{ConfigMemberName} returned null."
                : error;
            return false;
        }

        if (!TryGetMemberValue(
                config.GetType(),
                config,
                OfflineDataMemberName,
                InstanceMembers,
                out var offlineData,
                out error) ||
            offlineData is not IEnumerable characters)
        {
            error = string.IsNullOrWhiteSpace(error)
                ? $"{ConfigMemberName}.{OfflineDataMemberName} was not enumerable."
                : error;
            return false;
        }

        foreach (var candidate in characters)
        {
            if (candidate == null)
                continue;

            if (!TryReadUnsignedIntegerMember(candidate, ContentIdMemberName, out var candidateContentId, out error))
                return false;

            if (candidateContentId != localContentId)
                continue;

            characterData = candidate;
            return true;
        }

        error = $"AutoRetainer OfflineData did not contain local content ID {localContentId:X16}.";
        return false;
    }

    private static bool TryGetMemberValue(
        Type type,
        object? target,
        string memberName,
        BindingFlags flags,
        out object? value,
        out string error)
    {
        value = null;
        error = string.Empty;

        var property = type.GetProperty(memberName, flags);
        if (property != null)
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod == null)
            {
                error = $"{type.FullName}.{memberName} was not a readable property.";
                return false;
            }

            value = property.GetValue(target);
            return true;
        }

        var field = type.GetField(memberName, flags);
        if (field != null)
        {
            value = field.GetValue(target);
            return true;
        }

        error = $"{type.FullName}.{memberName} field/property was not available.";
        return false;
    }

    private static bool TryReadBooleanMember(
        object target,
        string memberName,
        out bool value,
        out string error)
    {
        value = false;
        if (!TryGetMemberValue(target.GetType(), target, memberName, InstanceMembers, out var raw, out error))
            return false;

        if (raw is bool boolean)
        {
            value = boolean;
            return true;
        }

        error = $"{target.GetType().FullName}.{memberName} was not a Boolean.";
        return false;
    }

    private static bool TryWriteBooleanMember(
        object target,
        string memberName,
        bool value,
        out string error)
    {
        error = string.Empty;
        var type = target.GetType();
        var property = type.GetProperty(memberName, InstanceMembers);
        if (property != null)
        {
            if (property.PropertyType != typeof(bool) || property.SetMethod == null || property.GetIndexParameters().Length != 0)
            {
                error = $"{type.FullName}.{memberName} was not a writable Boolean property.";
                return false;
            }

            property.SetValue(target, value);
            return true;
        }

        var field = type.GetField(memberName, InstanceMembers);
        if (field != null)
        {
            if (field.FieldType != typeof(bool) || field.IsInitOnly || field.IsLiteral)
            {
                error = $"{type.FullName}.{memberName} was not a writable Boolean field.";
                return false;
            }

            field.SetValue(target, value);
            return true;
        }

        error = $"{type.FullName}.{memberName} field/property was not available.";
        return false;
    }

    private static bool TryValidateWritableBooleanMember(
        object target,
        string memberName,
        out string error)
    {
        error = string.Empty;
        var type = target.GetType();
        var property = type.GetProperty(memberName, InstanceMembers);
        if (property != null)
        {
            if (property.PropertyType == typeof(bool) &&
                property.SetMethod != null &&
                property.GetIndexParameters().Length == 0)
            {
                return true;
            }

            error = $"{type.FullName}.{memberName} was not a writable Boolean property.";
            return false;
        }

        var field = type.GetField(memberName, InstanceMembers);
        if (field != null)
        {
            if (field.FieldType == typeof(bool) && !field.IsInitOnly && !field.IsLiteral)
                return true;

            error = $"{type.FullName}.{memberName} was not a writable Boolean field.";
            return false;
        }

        error = $"{type.FullName}.{memberName} field/property was not available.";
        return false;
    }

    private static bool TryReadUnsignedIntegerMember(
        object target,
        string memberName,
        out ulong value,
        out string error)
    {
        value = 0;
        if (!TryGetMemberValue(target.GetType(), target, memberName, InstanceMembers, out var raw, out error))
            return false;

        switch (raw)
        {
            case byte byteValue:
                value = byteValue;
                return true;
            case ushort ushortValue:
                value = ushortValue;
                return true;
            case uint uintValue:
                value = uintValue;
                return true;
            case ulong ulongValue:
                value = ulongValue;
                return true;
            case sbyte sbyteValue when sbyteValue >= 0:
                value = (ulong)sbyteValue;
                return true;
            case short shortValue when shortValue >= 0:
                value = (ulong)shortValue;
                return true;
            case int intValue when intValue >= 0:
                value = (ulong)intValue;
                return true;
            case long longValue when longValue >= 0:
                value = (ulong)longValue;
                return true;
            default:
                error = $"{target.GetType().FullName}.{memberName} was not an unsigned content ID.";
                return false;
        }
    }

    private static string DescribeException(Exception ex)
    {
        var current = ex;
        while (current is TargetInvocationException && current.InnerException != null)
            current = current.InnerException;

        return $"{current.GetType().Name}: {current.Message}";
    }
}
