using Newtonsoft.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class VerminionLifecycleTests
{
    [Fact]
    public void FullStopPauseSurvivesReloadAndCloneWithoutPausingAnotherCharacter()
    {
        var stopped = new CharacterConfig { EnableVerminionQueue = true, VerminionPaused = true };
        var reloaded = JsonConvert.DeserializeObject<CharacterConfig>(JsonConvert.SerializeObject(stopped))!;
        var cloned = reloaded.Clone();
        var otherCharacter = JsonConvert.DeserializeObject<CharacterConfig>("{\"EnableVerminionQueue\":true}")!;

        Assert.True(reloaded.VerminionPaused);
        Assert.True(cloned.VerminionPaused);
        Assert.False(otherCharacter.VerminionPaused);
        cloned.VerminionPaused = false;
        Assert.True(reloaded.VerminionPaused);
        reloaded.ResetVerminionState();
        Assert.True(reloaded.VerminionPaused);
    }
}
