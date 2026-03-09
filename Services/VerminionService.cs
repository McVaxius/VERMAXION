using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Threading.Tasks;
using Vermaxion.Models;

namespace Vermaxion.Services;

public class VerminionService : IDisposable
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private bool _isRunning = false;
    private int _currentAttempt = 0;
    private DateTime _lastActionTime = DateTime.MinValue;
    private CharacterConfig? _currentCharacter;

    public bool IsRunning => _isRunning;
    public int CurrentAttempt => _currentAttempt;
    public VerminionState CurrentState
    {
        get => _config.CurrentState;
        private set => _config.CurrentState = value;
    }

    public VerminionService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
        _log.Information("VerminionService initialized");
    }

    public async Task ExecuteAutomation(CharacterConfig character)
    {
        if (_isRunning)
        {
            _log.Warning("Verminion automation already running");
            return;
        }

        if (!character.VarminionEnabled)
        {
            _log.Warning("Varminion automation not enabled for this character");
            return;
        }

        _currentCharacter = character;
        _isRunning = true;
        _currentAttempt = 0;
        CurrentState = VerminionState.Queuing;
        _lastActionTime = DateTime.Now;

        _log.Information($"Starting Verminion automation for {character.GetDisplayName()}");

        try
        {
            // Main automation loop
            while (_isRunning && _currentAttempt < character.VarminionAttempts)
            {
                Update();
                await Task.Delay(100); // Update every 100ms
            }

            if (_currentAttempt >= character.VarminionAttempts)
            {
                await HandleCompleted();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Error during Verminion automation for {character.GetDisplayName()}");
            CurrentState = VerminionState.Error;
        }
        finally
        {
            StopAutomation();
        }
    }

    public void StopAutomation()
    {
        if (!_isRunning)
        {
            _log.Warning("Verminion automation not running");
            return;
        }

        _isRunning = false;
        CurrentState = VerminionState.Idle;
        _currentAttempt = 0;
        _currentCharacter = null;

        _log.Information("Stopping Verminion automation");
    }

    public bool CanExecute(CharacterConfig character)
    {
        if (!character.VarminionEnabled) return false;

        // TODO: Add availability checks
        // Check if character is in appropriate location, has sufficient level, etc.
        return true;
    }

    public string GetStatus(CharacterConfig character)
    {
        if (!character.VarminionEnabled)
            return "Disabled";

        if (_isRunning && _currentCharacter?.CharacterId == character.CharacterId)
        {
            return $"Running: {CurrentState} (Attempt {_currentAttempt + 1}/{character.VarminionAttempts})";
        }

        return "Available";
    }

    private void Update()
    {
        if (!_isRunning || _currentCharacter == null)
            return;

        try
        {
            switch (CurrentState)
            {
                case VerminionState.Idle:
                    // Do nothing, wait for user input
                    break;

                case VerminionState.Queuing:
                    HandleQueuing();
                    break;

                case VerminionState.InQueuePop:
                    HandleQueuePop();
                    break;

                case VerminionState.InDuty:
                    HandleInDuty();
                    break;

                case VerminionState.Failing:
                    HandleFailing();
                    break;

                case VerminionState.Exiting:
                    HandleExiting();
                    break;

                case VerminionState.Completed:
                    HandleCompleted().Wait();
                    break;

                case VerminionState.Error:
                    HandleError();
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in VerminionService.Update");
            CurrentState = VerminionState.Error;
        }
    }

    private void HandleQueuing()
    {
        // TODO: Implement queue logic
        _log.Debug("Handling queuing state");
        
        // For now, just simulate queue pop after delay
        if (DateTime.Now - _lastActionTime > TimeSpan.FromMilliseconds(_currentCharacter!.VarminionQueueDelay))
        {
            CurrentState = VerminionState.InQueuePop;
            _lastActionTime = DateTime.Now;
            _log.Information("Queue popped (simulated)");
        }
    }

    private void HandleQueuePop()
    {
        // TODO: Implement queue pop acceptance
        _log.Debug("Handling queue pop state");
        
        // For now, just accept after delay
        if (DateTime.Now - _lastActionTime > TimeSpan.FromMilliseconds(_currentCharacter!.VarminionQueueDelay / 2))
        {
            CurrentState = VerminionState.InDuty;
            _lastActionTime = DateTime.Now;
            _log.Information("Accepted queue pop (simulated)");
        }
    }

    private void HandleInDuty()
    {
        // TODO: Implement duty detection and failure logic
        _log.Debug("Handling in-duty state");
        
        // For now, just fail after delay
        if (DateTime.Now - _lastActionTime > TimeSpan.FromSeconds(5))
        {
            CurrentState = VerminionState.Failing;
            _lastActionTime = DateTime.Now;
            _log.Information("Starting failure process (simulated)");
        }
    }

    private void HandleFailing()
    {
        // TODO: Implement failure detection
        _log.Debug("Handling failure state");
        
        // For now, just transition to exiting after delay
        if (DateTime.Now - _lastActionTime > TimeSpan.FromMilliseconds(_currentCharacter!.VarminionFailureDelay))
        {
            _currentAttempt++;
            CurrentState = VerminionState.Exiting;
            _lastActionTime = DateTime.Now;
            _log.Information($"Attempt {_currentAttempt} failed (simulated)");
        }
    }

    private void HandleExiting()
    {
        // TODO: Implement duty exit
        _log.Debug("Handling exiting state");
        
        // For now, just transition to next state after delay
        if (DateTime.Now - _lastActionTime > TimeSpan.FromSeconds(2))
        {
            if (_currentAttempt >= _currentCharacter!.VarminionAttempts)
            {
                CurrentState = VerminionState.Completed;
                _log.Information("All attempts completed");
            }
            else
            {
                CurrentState = VerminionState.Queuing;
                _log.Information($"Starting next attempt ({_currentAttempt + 1}/{_currentCharacter.VarminionAttempts})");
            }
            _lastActionTime = DateTime.Now;
        }
    }

    private async Task HandleCompleted()
    {
        // TODO: Implement AutoRetainer re-enable
        _log.Debug("Handling completed state");
        
        // Update statistics
        if (_currentCharacter != null)
        {
            _currentCharacter.Statistics.VarminionAttempts++;
            _currentCharacter.Statistics.VarminionCompletions++;
            _currentCharacter.Statistics.LastVarminion = DateTime.Now;
        }
        
        // For now, just stop automation after delay
        if (DateTime.Now - _lastActionTime > TimeSpan.FromSeconds(2))
        {
            _log.Information("Varminion automation completed successfully");
            StopAutomation();
        }
    }

    private void HandleError()
    {
        _log.Debug("Handling error state");
        
        // Stop automation on error
        StopAutomation();
    }

    public void Dispose()
    {
        StopAutomation();
        _log.Information("VerminionService disposed");
    }
}
