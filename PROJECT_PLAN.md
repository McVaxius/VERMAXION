# Vermaxion Plugin - Project Plan

## Project Overview
**Plugin Name:** Vermaxion  
**Purpose:** Automate Varminion duty roulette queuing with intentional failure mode  
**Type:** Dalamud Plugin for FFXIV  
**Complexity:** Basic (starter project)

## Technical Architecture

### Core Components
1. **Plugin.cs** - Main plugin entry point
2. **Configuration.cs** - Plugin settings and persistence
3. **Services/VerminionService.cs** - Core automation logic
4. **Windows/MainWindow.cs** - UI interface
5. **Models/AttemptData.cs** - Attempt tracking data

### Dependencies
- **Dalamud.NET.Sdk** - Plugin framework
- **ECommons** - UI components and utilities
- **Dalamud.Game** - Game API access
- **AutoRetainer IPC** - Integration with AutoRetainer

## Development Phases

### Phase 1: Basic Plugin Structure
**Goal:** Create working plugin that loads in-game

**Tasks:**
- [ ] Create basic plugin project structure
- [ ] Set up Dalamud.NET.Sdk and dependencies
- [ ] Implement Plugin.cs with basic initialization
- [ ] Create simple UI window that displays
- [ ] Add basic configuration system
- [ ] Test plugin loads and displays UI

**Deliverables:**
- Working plugin that loads in Dalamud
- Simple UI window with "Start" button
- Basic configuration persistence

### Phase 2: Duty Detection
**Goal:** Detect when player is in Varminion duty

**Tasks:**
- [ ] Implement territory type detection
- [ ] Add duty state monitoring
- [ ] Create Varminion-specific detection logic
- [ ] Handle duty completion/failure detection
- [ ] Add state machine for duty tracking

**Deliverables:**
- Varminion duty detection system
- State machine for duty progress
- Failure detection logic

### Phase 3: Queue Automation
**Goal:** Automate duty roulette queuing

**Tasks:**
- [ ] Implement duty finder interaction
- [ ] Add queue automation logic
- [ ] Handle queue pop detection
- [ ] Add accept/decline automation
- [ ] Implement retry logic for failed queues

**Deliverables:**
- Automated duty queuing
- Queue pop handling
- Retry system

### Phase 4: Failure Automation
**Goal:** Intentionally fail duties with detection

**Tasks:**
- [ ] Implement failure detection methods
- [ ] Add automated failure triggers
- [ ] Create attempt counting system
- [ ] Handle duty exit automation
- [ ] Add retry logic for next attempt

**Deliverables:**
- Automated failure detection
- Attempt counter system
- Duty exit automation

### Phase 5: AutoRetainer Integration
**Goal:** Integrate with AutoRetainer for final state

**Tasks:**
- [ ] Implement AutoRetainer IPC communication
- [ ] Add AutoRetainer state detection
- [ ] Create enable/disable automation
- [ ] Handle integration errors
- [ ] Add configuration options

**Deliverables:**
- AutoRetainer IPC integration
- Enable/disable automation
- Error handling for integration

### Phase 6: Polish and Testing
**Goal:** Finalize plugin with proper error handling

**Tasks:**
- [ ] Add comprehensive error handling
- [ ] Implement logging system
- [ ] Add status display improvements
- [ ] Create user documentation
- [ ] Perform end-to-end testing

**Deliverables:**
- Complete, tested plugin
- User documentation
- Error handling and logging

## Technical Considerations

### State Management
```csharp
public enum VerminionState
{
    Idle,
    Queuing,
    InDuty,
    Failing,
    Exiting,
    Completed,
    Error
}
```

### Configuration Structure
```csharp
public class VerminionConfiguration
{
    public int MaxAttempts { get; set; } = 5;
    public bool EnableAutoRetainer { get; set; } = true;
    public int QueueRetryDelay { get; set; } = 5000;
    public int FailureDelay { get; set; } = 3000;
}
```

### Key APIs to Use
- **DutyFinder** - Queue management
- **ClientState** - Territory and duty detection
- **Condition** - Game state flags
- **ChatGui** - Message monitoring
- **TargetManager** - Object interaction

### Error Handling Strategy
- Try-catch blocks around all automation
- Graceful degradation on failures
- User notification of errors
- Logging for debugging

## Testing Strategy

### Unit Testing
- [ ] Configuration loading/saving
- [ ] State machine transitions
- [ ] IPC communication

### Integration Testing
- [ ] Plugin loading/unloading
- [ ] Duty detection accuracy
- [ ] Queue automation reliability
- [ ] AutoRetainer integration

### User Acceptance Testing
- [ ] End-to-end workflow
- [ ] Error recovery
- [ ] Performance impact
- [ ] User interface usability

## Risk Assessment

### Technical Risks
- **Duty detection changes** - Game updates may break detection
- **AutoRetainer compatibility** - IPC interface changes
- **Performance impact** - Continuous monitoring overhead

### Mitigation Strategies
- Version-specific compatibility checks
- Fallback detection methods
- Configurable update intervals
- Performance monitoring

## Success Criteria
- [ ] Plugin loads successfully in Dalamud
- [ ] Can detect Varminion duty reliably
- [ ] Automates queuing and failure 5 times
- [ ] Re-enables AutoRetainer after completion
- [ ] Handles errors gracefully
- [ ] Provides clear user feedback

## Timeline Estimate
- **Phase 1:** 1-2 days (basic structure)
- **Phase 2:** 2-3 days (duty detection)
- **Phase 3:** 2-3 days (queue automation)
- **Phase 4:** 2-3 days (failure automation)
- **Phase 5:** 1-2 days (AutoRetainer integration)
- **Phase 6:** 2-3 days (polish and testing)

**Total Estimated Time:** 10-16 days

## Resources and References
- [Sample Plugin Repository](https://github.com/goatcorp/SamplePlugin)
- [Dalamud API Documentation](https://github.com/goatcorp/Dalamud)
- [AutoRetainer IPC Documentation](https://github.com/PunishXIV/AutoRetainer)
- [ECommons Documentation](https://github.com/ECommonsDev/ECommons)

## Next Steps
1. Review and approve project plan
2. Set up development environment
3. Begin Phase 1 implementation
4. Create initial project structure
5. Test basic plugin loading
