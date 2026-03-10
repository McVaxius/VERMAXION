using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Logging;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

// [WIP] - Implementation for highest combat job selector, needs SimpleTweaks testing
public class HighestCombatJobService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IPlayerState playerState;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    
    private DateTime lastAction = DateTime.MinValue;
    private bool isRunning = false;
    
    // Combat job IDs (DOW/DOM only - matching FUTA's which_cj() approach)
    private static readonly uint[] CombatJobs = new uint[]
    {
        // First loop: 0-7 (base classes)
        0,  // ADV (Adventurer)
        1,  // GLA (Paladin)
        2,  // PGL (Monk)
        3,  // MRD (Warrior)
        4,  // ARC (Bard)
        5,  // LNC (Dragoon)
        6,  // CNJ (White Mage)
        7,  // THM (Black Mage)
        
        // Second loop: 19-29 (expanded jobs)
        19, // PLD (Paladin)
        20, // MNK (Monk)
        21, // WAR (Warrior)
        22, // DRG (Dragoon)
        23, // BRD (Bard)
        24, // WHM (White Mage)
        25, // BLM (Black Mage)
        26, // ACN (Arcanist)
        27, // SMN (Summoner)
        28, // SCH (Scholar)
        29, // ROG (Ninja)
        
        // Additional combat jobs (not in FUTA but needed)
        30, // NIN (Ninja)
        31, // MCH (Machinist)
        32, // DRK (Dark Knight)
        33, // AST (Astrologian)
        34, // SAM (Samurai)
        35, // RPR (Reaper)
        36, // SGE (Sage)
    };

    public HighestCombatJobService(ICommandManager commandManager, IPluginLog log, IPlayerState playerState, IClientState clientState, IObjectTable objectTable)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.playerState = playerState;
        this.clientState = clientState;
        this.objectTable = objectTable;
    }

    public void RunTask()
    {
        if (isRunning)
        {
            log.Information("[HighestCombatJob] Already running");
            return;
        }

        log.Information("[HighestCombatJob] Starting highest combat job selection");
        isRunning = true;
        lastAction = DateTime.UtcNow;
        
        try
        {
            SelectHighestCombatJob();
        }
        catch (Exception ex)
        {
            log.Error($"[HighestCombatJob] Error: {ex.Message}");
        }
        finally
        {
            isRunning = false;
            log.Information("[HighestCombatJob] Task complete");
        }
    }

    private void SelectHighestCombatJob()
    {
        var highestJob = GetHighestCombatJob();
        if (highestJob == null)
        {
            log.Warning("[HighestCombatJob] No combat jobs found");
            return;
        }

        log.Information($"[HighestCombatJob] Highest combat job: {highestJob.Name} (Level {highestJob.Level}, ID {highestJob.JobId})");
        
        // Use SimpleTweaks command to switch to highest job
        // This requires SimpleTweaks plugin with job switching functionality
        CommandHelper.SendCommand($"/job {highestJob.JobId}");
        
        log.Information($"[HighestCombatJob] Sent command to switch to job {highestJob.JobId}");
    }

    private CombatJobInfo? GetHighestCombatJob()
    {
        var combatJobs = new List<CombatJobInfo>();
        
        // Get all combat jobs and their levels
        foreach (var jobId in CombatJobs)
        {
            var level = GetJobLevel(jobId);
            if (level > 0) // Job is unlocked
            {
                var jobName = GetJobName(jobId);
                combatJobs.Add(new CombatJobInfo(jobId, jobName, level));
            }
        }

        if (!combatJobs.Any())
        {
            return null;
        }

        // Find the job with the highest level
        var highestJob = combatJobs
            .OrderByDescending(j => j.Level)
            .ThenBy(j => j.JobId) // Tie-breaker by job ID
            .First();

        return highestJob;
    }

    private int GetJobLevel(uint jobId)
    {
        try
        {
            // Get current job info
            var currentClassJob = playerState?.ClassJob;
            var currentJobId = currentClassJob?.RowId ?? 0;
            var currentLevel = (int)(playerState?.Level ?? 0);
            
            log.Debug($"[HighestCombatJob] Current job: ID={currentJobId}, Level={currentLevel}");
            
            // If this is the current job, return its level
            if (currentJobId == jobId)
            {
                log.Debug($"[HighestCombatJob] Job {jobId} is current, level: {currentLevel}");
                return currentLevel;
            }

            // For other jobs, we need a different approach
            log.Debug($"[HighestCombatJob] Job {jobId} not current, need to implement multi-job level detection");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error($"[HighestCombatJob] Error getting job level for {jobId}: {ex.Message}");
            return 0;
        }
    }

    private string GetJobName(uint jobId)
    {
        return jobId switch
        {
            1 or 26 => "Paladin",
            2 or 27 => "Monk", 
            3 or 28 => "Warrior",
            4 or 29 => "Dragoon",
            5 or 30 => "Bard",
            6 or 31 => "White Mage",
            7 or 32 => "Black Mage",
            33 => "Arcanist",
            34 => "Summoner",
            35 => "Scholar",
            36 or 37 => "Ninja",
            38 => "Machinist",
            39 => "Dark Knight",
            40 => "Astrologian",
            41 => "Samurai",
            42 => "Reaper",
            43 => "Sage",
            _ => $"Job{jobId}"
        };
    }

    public void Dispose()
    {
        isRunning = false;
    }

    public void Update()
    {
        // This service doesn't need continuous updates - it's a one-shot operation
        // The Update() method is required for consistency with other services
    }
}

public record CombatJobInfo(uint JobId, string Name, int Level);
