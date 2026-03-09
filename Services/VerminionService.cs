using Dalamud.Plugin.Services;
using System;
using System.Threading.Tasks;

namespace Vermaxion.Services;

public class VerminionService : IDisposable
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private bool _isRunning = false;
    private int _currentAttempt = 0;
    private DateTime _lastActionTime = DateTime.MinValue;
    private VerminionState _currentState = VerminionState.Idle;

    public bool IsRunning => _isRunning;
    public int CurrentAttempt => _currentAttempt;
    public VerminionState CurrentState
    {
        get => _currentState;
        private set => _currentState = value;
    }
    public int MaxAttempts => _config.MaxAttempts;

    public VerminionService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
        _log.Information("[Vermaxion] VerminionService initialized");
    }

    public void StartAutomation()
    {
        if (_isRunning)
        {
            _log.Warning("[Vermaxion] Automation already running");
            return;
        }

        _isRunning = true;
        _currentAttempt = 0;
        CurrentState = VerminionState.Queuing;
        _lastActionTime = DateTime.Now;

        _log.Information("[Vermaxion] Starting Verminion automation");

        // Start async update loop
        _ = Task.Run(async () =>
        {
            try
            {
                while (_isRunning && _currentAttempt < _config.MaxAttempts)
                {
                    Update();
                    await Task.Delay(100);
                }

                if (_currentAttempt >= _config.MaxAttempts)
                {
                    HandleCompleted();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Vermaxion] Error during automation");
                CurrentState = VerminionState.Error;
            }
        });
    }

    public void StopAutomation()
    {
        if (!_isRunning)
        {
            _log.Warning("[Vermaxion] Automation not running");
            return;
        }

        _isRunning = false;
        CurrentState = VerminionState.Idle;
        _currentAttempt = 0;

        _log.Information("[Vermaxion] Stopping automation");
    }

    public void ResetAutomation()
    {
        StopAutomation();
        _currentAttempt = 0;
        CurrentState = VerminionState.Idle;
        _log.Information("[Vermaxion] Reset automation");
    }

    public string GetStatusText()
    {
        if (!_isRunning)
            return "Idle";

        return $"{CurrentState}";
    }

    private void Update()
    {
        if (!_isRunning)
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
                    HandleCompleted();
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
        // Phase 1: Simulated queue
        if (DateTime.Now - _lastActionTime > TimeSpan.FromMilliseconds(_config.QueueRetryDelay))
        {
            CurrentState = VerminionState.InQueuePop;
            _lastActionTime = DateTime.Now;
            _log.Information("[Vermaxion] Queue popped (simulated)");
        }
    }

    private void HandleQueuePop()
    {
        // Phase 1: Simulated acceptance
        if (DateTime.Now - _lastActionTime > TimeSpan.FromMilliseconds(_config.QueueRetryDelay / 2))
        {
            CurrentState = VerminionState.InDuty;
            _lastActionTime = DateTime.Now;
            _log.Information("[Vermaxion] Accepted queue pop (simulated)");
        }
    }

    private void HandleInDuty()
    {
        // Phase 1: Simulated duty
        if (DateTime.Now - _lastActionTime > TimeSpan.FromSeconds(5))
        {
            CurrentState = VerminionState.Failing;
            _lastActionTime = DateTime.Now;
            _log.Information("[Vermaxion] Starting failure process (simulated)");
        }
    }

    private void HandleFailing()
    {
        // Phase 1: Simulated failure
        if (DateTime.Now - _lastActionTime > TimeSpan.FromMilliseconds(_config.FailureDelay))
        {
            _currentAttempt++;
            CurrentState = VerminionState.Exiting;
            _lastActionTime = DateTime.Now;
            _log.Information($"[Vermaxion] Attempt {_currentAttempt} failed (simulated)");
        }
    }

    private void HandleExiting()
    {
        // Phase 1: Simulated exit
        if (DateTime.Now - _lastActionTime > TimeSpan.FromSeconds(2))
        {
            if (_currentAttempt >= _config.MaxAttempts)
            {
                CurrentState = VerminionState.Completed;
                _log.Information("[Vermaxion] All attempts completed");
            }
            else
            {
                CurrentState = VerminionState.Queuing;
                _log.Information($"[Vermaxion] Starting next attempt ({_currentAttempt + 1}/{_config.MaxAttempts})");
            }
            _lastActionTime = DateTime.Now;
        }
    }

    private void HandleCompleted()
    {
        // Phase 1: Simulated completion
        _log.Information("[Vermaxion] Automation completed successfully");
        
        if (_config.EnableAutoRetainer)
        {
            _log.Information("[Vermaxion] AutoRetainer re-enable would happen here (Phase 5)");
        }
        
        StopAutomation();
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
        _log.Information("[Vermaxion] VerminionService disposed");
    }
}
