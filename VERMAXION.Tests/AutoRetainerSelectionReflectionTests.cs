using System;
using System.Reflection;
using VERMAXION.IPC;
using Xunit;

namespace VERMAXION.Tests
{
    public sealed class AutoRetainerSelectionReflectionTests
    {
        public AutoRetainerSelectionReflectionTests() => AutoRetainer.Modules.OfflineDataManager.Reset();

        [Fact]
        public void ResolvesTheExistingStaticTwoBooleanSaveOverload()
        {
            Assert.True(AutoRetainerSelectionReflection.TryResolveSaveMethod(
                new AutoRetainer.AutoRetainer(), out var save, out var error), error);

            Assert.True(save.IsStatic);
            Assert.Equal(new[] { typeof(bool), typeof(bool) },
                Array.ConvertAll(save.GetParameters(), parameter => parameter.ParameterType));
            save.Invoke(null, [false, true]);

            Assert.Equal(1, AutoRetainer.Modules.OfflineDataManager.WriteCount);
            Assert.False(AutoRetainer.Modules.OfflineDataManager.LastWriteGatherables);
            Assert.True(AutoRetainer.Modules.OfflineDataManager.LastSaveConfig);
        }

        [Fact]
        public void MissingSaveTypeFailsBeforeAnyNativeInvocation()
        {
            Assert.False(AutoRetainerSelectionReflection.TryResolveSaveMethod(new object(), out _, out var error));
            Assert.Contains("OfflineDataManager", error);
            Assert.Equal(0, AutoRetainer.Modules.OfflineDataManager.WriteCount);
        }

        [Fact]
        public void NativeSaveFailureRemainsObservable()
        {
            AutoRetainer.Modules.OfflineDataManager.ThrowOnWrite = true;
            Assert.True(AutoRetainerSelectionReflection.TryResolveSaveMethod(
                new AutoRetainer.AutoRetainer(), out var save, out _));

            var failure = Assert.Throws<TargetInvocationException>(() => save.Invoke(null, [false, true]));
            Assert.Contains("save failed", failure.GetBaseException().Message);
            Assert.Equal(1, AutoRetainer.Modules.OfflineDataManager.WriteCount);
        }
    }
}

namespace AutoRetainer
{
    internal sealed class AutoRetainer { }
}

namespace AutoRetainer.Modules
{
    internal static class OfflineDataManager
    {
        public static int WriteCount { get; private set; }
        public static bool LastWriteGatherables { get; private set; }
        public static bool LastSaveConfig { get; private set; }
        public static bool ThrowOnWrite { get; set; }

        internal static void WriteOfflineData(bool writeGatherables, bool saveConfig)
        {
            WriteCount++;
            LastWriteGatherables = writeGatherables;
            LastSaveConfig = saveConfig;
            if (ThrowOnWrite)
                throw new InvalidOperationException("save failed");
        }

        internal static void WriteOfflineData(int unrelated, int overload)
            => throw new InvalidOperationException("Wrong save overload");

        public static void Reset()
        {
            WriteCount = 0;
            LastWriteGatherables = false;
            LastSaveConfig = false;
            ThrowOnWrite = false;
        }
    }
}
