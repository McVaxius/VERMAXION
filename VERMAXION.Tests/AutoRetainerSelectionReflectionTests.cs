using System;
using System.Collections.Generic;
using VERMAXION.IPC;
using Xunit;

namespace VERMAXION.Tests
{
    public sealed class AutoRetainerSelectionReflectionTests
    {
        private const ulong ContentId = 0x4000174C2E9539;

        public AutoRetainerSelectionReflectionTests()
        {
            AutoRetainer.AutoRetainer.C.OfflineData.Clear();
            AutoRetainer.Modules.OfflineDataManager.Reset();
        }

        [Fact]
        public void ReadsWritesVerifiesAndPersistsCurrentCharacterSelection()
        {
            var current = new AutoRetainer.FakeOfflineCharacterData
            {
                CID = ContentId,
                Enabled = false,
            };
            AutoRetainer.AutoRetainer.C.OfflineData.Add(new AutoRetainer.FakeOfflineCharacterData
            {
                CID = ContentId + 1,
                Enabled = true,
            });
            AutoRetainer.AutoRetainer.C.OfflineData.Add(current);
            var plugin = new AutoRetainer.AutoRetainer();

            var before = AutoRetainerSelectionReflection.Read(plugin, ContentId);
            var write = AutoRetainerSelectionReflection.Write(plugin, ContentId, enabled: true);
            var after = AutoRetainerSelectionReflection.Read(plugin, ContentId);

            Assert.True(before.Success);
            Assert.False(before.Enabled);
            Assert.True(write.Success);
            Assert.True(write.Enabled);
            Assert.True(write.SaveInvoked);
            Assert.True(after.Success);
            Assert.True(after.Enabled);
            Assert.Equal(1, AutoRetainer.Modules.OfflineDataManager.WriteCount);
            Assert.False(AutoRetainer.Modules.OfflineDataManager.LastWriteGatherables);
            Assert.True(AutoRetainer.Modules.OfflineDataManager.LastSaveConfig);
        }

        [Fact]
        public void SaveExceptionReturnsFailureAfterVerifiedInMemoryWrite()
        {
            var current = new AutoRetainer.FakeOfflineCharacterData
            {
                CID = ContentId,
                Enabled = false,
            };
            AutoRetainer.AutoRetainer.C.OfflineData.Add(current);
            AutoRetainer.Modules.OfflineDataManager.ThrowOnWrite = true;

            var result = AutoRetainerSelectionReflection.Write(
                new AutoRetainer.AutoRetainer(),
                ContentId,
                enabled: true);

            Assert.False(result.Success);
            Assert.True(result.Enabled);
            Assert.True(result.SaveInvoked);
            Assert.True(current.Enabled);
            Assert.Contains("save failed", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MissingCharacterAndMissingPluginTypeFailClosed()
        {
            var missingCharacter = AutoRetainerSelectionReflection.Read(
                new AutoRetainer.AutoRetainer(),
                ContentId);
            var missingPluginType = AutoRetainerSelectionReflection.Read(new object(), ContentId);

            Assert.False(missingCharacter.Success);
            Assert.Contains(ContentId.ToString("X16"), missingCharacter.Error, StringComparison.Ordinal);
            Assert.False(missingPluginType.Success);
            Assert.Contains("AutoRetainer.AutoRetainer", missingPluginType.Error, StringComparison.Ordinal);
        }

        [Fact]
        public void UnwritableEnabledMemberFailsBeforePersistence()
        {
            var current = new AutoRetainer.FakeReadOnlyOfflineCharacterData(ContentId, enabled: false);
            AutoRetainer.AutoRetainer.C.OfflineData.Add(current);

            var result = AutoRetainerSelectionReflection.Write(
                new AutoRetainer.AutoRetainer(),
                ContentId,
                enabled: true);

            Assert.False(result.Success);
            Assert.False(current.Enabled);
            Assert.False(result.SaveInvoked);
            Assert.Equal(0, AutoRetainer.Modules.OfflineDataManager.WriteCount);
            Assert.Contains("writable Boolean", result.Error, StringComparison.Ordinal);
        }
    }
}

namespace AutoRetainer
{
    internal sealed class AutoRetainer
    {
        internal static FakeConfig C { get; } = new();
    }

    internal sealed class FakeConfig
    {
        public List<object> OfflineData { get; } = [];
    }

    internal sealed class FakeOfflineCharacterData
    {
        public ulong CID { get; set; }
        public bool Enabled { get; set; }
    }

    internal sealed class FakeReadOnlyOfflineCharacterData(ulong cid, bool enabled)
    {
        public ulong CID { get; } = cid;
        public bool Enabled { get; } = enabled;
    }
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

        public static void Reset()
        {
            WriteCount = 0;
            LastWriteGatherables = false;
            LastSaveConfig = false;
            ThrowOnWrite = false;
        }
    }
}
