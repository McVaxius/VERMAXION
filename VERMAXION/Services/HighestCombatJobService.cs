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
    
    // Combat job IDs (DOW/DOM only - actual combat jobs)
    private static readonly uint[] CombatJobs = new uint[]
    {
        // Disciples of War (DOW)
        1,  // GLA -> Paladin (26)
        2,  // PGL -> Monk (27)
        3,  // MRD -> Warrior (28)
        4,  // LNC -> Dragoon (29)
        5,  // ARC -> Bard (30)
        
        // Disciples of Magic (DOM)
        6,  // CNJ -> White Mage (31)
        7,  // THM -> Black Mage (32)
        26, // Paladin
        27, // Monk
        28, // Warrior
        29, // Dragoon
        30, // Bard
        31, // White Mage
        32, // Black Mage
        33, // Arcanist -> Summoner (34) / Scholar (35)
        34, // Summoner
        35, // Scholar
        36, // ROG -> Ninja (37)
        37, // Ninja
        38, // Machinist
        39, // Dark Knight
        40, // Astrologian
        41, // Samurai
        42, // Reaper
        43, // Sage
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
            // Use PlayerState to get current job level
            // For now, we can only get the current job's level reliably
            var currentClassJob = playerState?.ClassJob;
            if (currentClassJob?.RowId == jobId)
            {
                log.Debug($"[HighestCombatJob] Current job {jobId} level: {playerState.Level}");
                return (int)playerState.Level;
            }

            // For other jobs, we need a different approach
            // Let's try using ObjectTable to find the player and get job levels
            if (objectTable == null)
            {
                log.Warning("[HighestCombatJob] Could not get ObjectTable");
                return 0;
            }

            var player = objectTable[0];
            if (player == null)
            {
                log.Warning("[HighestCombatJob] Could not get player from ObjectTable");
                return 0;
            }

            // Try to get job levels from player character
            // This is a simplified approach - may need refinement
            log.Debug($"[HighestCombatJob] Job {jobId} not current, returning 0 for now");
            return 0; // Will only detect current job for now
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
