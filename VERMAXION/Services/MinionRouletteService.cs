using System;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class MinionRouletteService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly IChatGui chatGui;

    private enum MinionState { Idle, Summoning, WaitingForCast, Complete, Failed, OnCooldown }
    private MinionState state = MinionState.Idle;
    private DateTime stateEnteredAt;
    private bool castDetected = false;
    private bool cooldownDetected = false;

    public bool IsComplete => state == MinionState.Complete;
    public bool IsFailed => state == MinionState.Failed;
    public bool IsIdle => state == MinionState.Idle;
    public string StatusText => state.ToString();

    public MinionRouletteService(ICommandManager commandManager, IPluginLog log, IClientState clientState, IChatGui chatGui)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
        this.chatGui = chatGui;
        
        // Subscribe to chat messages for cast detection
        chatGui.ChatMessage += OnChatMessage;
    }

    private void SetState(MinionState newState)
    {
        log.Information($"[MinionRoulette] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        
        // Reset detection flags when starting new summon
        if (newState == MinionState.Summoning)
        {
            castDetected = false;
            cooldownDetected = false;
        }
    }
    
    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (state != MinionState.WaitingForCast) return;
        
        var messageText = message.TextValue;
        
        // Detect successful minion summon
        if (messageText.Contains("You summon the minion"))
        {
            castDetected = true;
            log.Information($"[MinionRoulette] Cast detected: {messageText}");
        }
        // Detect cooldown message
        else if (messageText.Contains("Minion roulette is on cooldown"))
        {
            cooldownDetected = true;
            log.Information($"[MinionRoulette] Cooldown detected: {messageText}");
        }
        // Detect location restriction
        else if (messageText.Contains("You cannot summon minions here"))
        {
            log.Warning($"[MinionRoulette] Location restriction: {messageText}");
        }
    }

    public void Start()
    {
        SetState(MinionState.Summoning);
        log.Information("[MinionRoulette] Firing minion roulette");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Minion Roulette triggered");
        Start();
    }

    public void Reset()
    {
        SetState(MinionState.Idle);
    }

    public void Update()
    {
        if (state == MinionState.Idle || state == MinionState.Complete || state == MinionState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case MinionState.Summoning:
                commandManager.ProcessCommand("/generalaction \"Minion Roulette\"");
                SetState(MinionState.WaitingForCast);
                break;

            case MinionState.WaitingForCast:
                if (elapsed > 5.0)
                {
                    if (castDetected)
                    {
                        log.Information("[MinionRoulette] Minion roulette complete (cast detected)");
                        SetState(MinionState.Complete);
                    }
                    else if (cooldownDetected)
                    {
                        log.Information("[MinionRoulette] Minion roulette on cooldown");
                        SetState(MinionState.OnCooldown);
                    }
                    else
                    {
                        log.Warning("[MinionRoulette] Minion roulette failed - no cast detected after 5s");
                        SetState(MinionState.Failed);
                    }
                }
                break;
                
            case MinionState.OnCooldown:
                // Cooldown state - just mark as complete for retry later
                log.Information("[MinionRoulette] Minion roulette on cooldown - will retry later");
                SetState(MinionState.Complete);
                break;
        }
    }

    public void Dispose() 
    {
        chatGui.ChatMessage -= OnChatMessage;
    }
}
