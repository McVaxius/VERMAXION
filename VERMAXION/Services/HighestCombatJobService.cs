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

        // Check if current job is a DoL/DoH job
        uint currentJobId = 0;
        var classJob = playerState?.ClassJob;
        if (classJob.HasValue)
        {
            currentJobId = classJob.Value.RowId;
        }
        
        if (IsDoLorDoH(currentJobId))
        {
            log.Information($"[HighestCombatJob] Current job {currentJobId} is DoL/DoH, switching to combat job first");
            
            // Switch to a known combat job (PLD) using equipjob command
            CommandHelper.SendCommand("/equipjob pld");
            log.Information("[HighestCombatJob] Sent command to switch to Paladin (/equipjob pld)");
            
            // Wait a moment for job switch, then continue
            // The next Update() call will handle the actual logic
            return;
        }

        // If already on combat job, proceed normally
        SelectHighestCombatJob();
        isRunning = false;
    }

    private bool IsDoLorDoH(uint jobId)
    {
        // DoL jobs: 16 (MIN), 17 (BOT), 18 (FSH)
        // DoH jobs: 8 (CRP), 9 (BSM), 10 (ARM), 11 (GSM), 12 (LTW), 13 (WVR), 14 (ALC), 15 (CUL)
        return (jobId >= 8 && jobId <= 18);
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
        
        // Send command to switch to the highest combat job using equipjob
        var jobCommand = GetEquipJobCommand(highestJob.JobId);
        CommandHelper.SendCommand($"/equipjob {jobCommand}");
        log.Information($"[HighestCombatJob] Sent command to switch to {highestJob.Name} (/equipjob {jobCommand})");
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
            // Get current job info for logging
            uint currentJobId = 0;
            var classJob = playerState?.ClassJob;
            if (classJob.HasValue)
            {
                currentJobId = classJob.Value.RowId;
            }
            var currentLevel = (int)(playerState?.Level ?? 0);
            
            log.Debug($"[HighestCombatJob] Current job: ID={currentJobId}, Level={currentLevel}");
            
            // If this is the current job, return its level
            if (currentJobId == jobId)
            {
                log.Debug($"[HighestCombatJob] Job {jobId} is current, level: {currentLevel}");
                return currentLevel;
            }

            // For other jobs, we need to access the full job list
            // This is the key challenge - FUTA uses Player.GetJob(i).Level but we need the Dalamud equivalent
            
            // Try using ClientState to get character data
            var localPlayer = clientState?.LocalPlayer;
            if (localPlayer == null)
            {
                log.Debug($"[HighestCombatJob] No local player available for job {jobId}");
                return 0;
            }

            // Experimental: Try to access job levels through character data
            // This is where we need the right API like FUTA's Player.GetJob(i)
            log.Debug($"[HighestCombatJob] Job {jobId} not current, multi-job detection not yet implemented");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error($"[HighestCombatJob] Error getting job level for {jobId}: {ex.Message}");
            return 0;
        }
    }

    private string GetEquipJobCommand(uint jobId)
    {
        return jobId switch
        {
            1 or 26 or 19 => "pld",
            2 or 27 or 20 => "mnk", 
            3 or 28 or 21 => "war",
            4 or 29 or 22 => "drg",
            5 or 30 or 23 => "brd",
            6 or 31 or 24 => "whm",
            7 or 32 or 25 => "blm",
            33 => "acn",
            34 => "smn",
            35 => "sch",
            36 => "rog",
            37 => "nin",
            38 => "mch",
            39 => "drk",
            40 => "ast",
            41 => "sam",
            42 => "rpr",
            43 => "sge",
            _ => "pld" // fallback
        };
    }

    private bool IsCombatJob(uint jobId)
    {
        // Check if this is a combat job (DOW/DOM)
        return CombatJobs.Contains(jobId);
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
        if (!isRunning) return;

        var elapsed = (DateTime.UtcNow - lastAction).TotalSeconds;
        
        // Check if we're waiting for a job switch to complete
        if (elapsed > 3.0) // 3 second wait for job switch
        {
            // Job switch should be complete, now proceed with combat job detection
            log.Information("[HighestCombatJob] Job switch wait completed, proceeding with combat job detection");
            SelectHighestCombatJob();
            isRunning = false;
            log.Information("[HighestCombatJob] Task complete");
        }
    }
}

public record CombatJobInfo(uint JobId, string Name, int Level);
