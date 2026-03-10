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
    
    private DateTime lastAction = DateTime.MinValue;
    private bool isRunning = false;
    
    // Combat job IDs (DOW/DOM only)
    private static readonly uint[] CombatJobs = new uint[]
    {
        // Disciples of War (DOW)
        1,  // GLA (Paladin)
        2,  // PGL (Monk) 
        3,  // MRD (Warrior)
        4,  // LNC (Dragoon)
        5,  // ARC (Bard)
        6,  // CNJ (White Mage - also DOW)
        7,  // THM (Black Mage - also DOW)
        8,  // CRP (Carpenter - DoL, excluded)
        9,  // BSM (Blacksmith - DoL, excluded)
        10, // ARM (Armorer - DoL, excluded)
        11, // GSM (Goldsmith - DoL, excluded)
        12, // LTW (Leatherworker - DoL, excluded)
        13, // WVR (Weaver - DoL, excluded)
        14, // ALC (Alchemist - DoL, excluded)
        15, // CUL (Culinary - DoL, excluded)
        16, // MIN (Miner - DoL, excluded)
        17, // BOT (Botanist - DoL, excluded)
        18, // FSH (Fisher - DoL, excluded)
        // Disciples of Magic (DOM)
        26, // PLD (Paladin)
        27, // MNK (Monk)
        28, // WAR (Warrior)
        29, // DRG (Dragoon)
        30, // BRD (Bard)
        31, // WHM (White Mage)
        32, // BLM (Black Mage)
        33, // ACN (Arcanist)
        34, // SMN (Summoner)
        35, // SCH (Scholar)
        36, // ROG (Ninja)
        37, // NIN (Ninja)
        38, // MCH (Machinist)
        39, // DRK (Dark Knight)
        40, // AST (Astrologian)
        41, // SAM (Samurai)
        42, // RPR (Reaper)
        43, // SGE (Sage)
    };

    public HighestCombatJobService(ICommandManager commandManager, IPluginLog log, IPlayerState playerState)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.playerState = playerState;
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
            // Use player state to get job level
            // This is a simplified approach - actual implementation may vary
            var classJob = playerState?.ClassJob;
            if (classJob?.RowId == jobId)
            {
                return (int)playerState.Level;
            }
            
            // For other jobs, we'd need to access the full job list
            // This is a placeholder - actual implementation would query the job data
            return 0; // Placeholder
        }
        catch
        {
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
