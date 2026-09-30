// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for VERMAXION.
using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace VERMAXION.CustomDeliveries;

public unsafe class Achievements : IDisposable
{
    public event Action<uint, uint, uint>? AchievementProgress;

    private readonly Hook<Achievement.Delegates.ReceiveAchievementProgress> _hook;
    private uint? requestedAchievement;
    private ulong requestingCharacter;

    public Achievements()
    {
        _hook = Service.Hook.HookFromAddress<Achievement.Delegates.ReceiveAchievementProgress>(Achievement.Addresses.ReceiveAchievementProgress.Value, ReceiveAchievementDetour);
        _hook.Enable();
    }

    public void Dispose()
    {
        _hook.Dispose();
    }

    public void Request(uint id)
    {
        var ui = UIState.Instance();
        if (ui != null && ui->PlayerState.IsLoaded && ui->Achievement.ProgressRequestState != Achievement.AchievementState.Requested)
        {
            requestedAchievement = id;
            requestingCharacter = Service.PlayerState.ContentId;
            ui->Achievement.RequestAchievementProgress(id);
        }
    }

    private void ReceiveAchievementDetour(Achievement* self, uint id, uint current, uint max)
    {
        _hook.Original(self, id, current, max);
        if (requestedAchievement != id) return;
        requestedAchievement = null;
        if (requestingCharacter == 0 || requestingCharacter != Service.PlayerState.ContentId
            || !Service.ClientState.IsLoggedIn) return;
        try { AchievementProgress?.Invoke(id, current, max); }
        catch (Exception ex) { Service.Log.Error(ex, "Custom delivery achievement update failed"); }
    }
}
