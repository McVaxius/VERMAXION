# Vermaxion Plugin

## Overview
A Dalamud plugin for FFXIV that automates Varminion duty roulette queuing with intentional failure mode for testing purposes.

## Purpose
- Queue for Varminion duty roulette
- Fail the duty 5 times total
- Turn AutoRetainer back on after completion
- Simple automation for testing framework

## Features
### Phase 1: Basic Functionality
- [ ] Plugin initialization and UI
- [ ] Duty detection (Varminion)
- [ ] Queue automation
- [ ] Failure detection
- [ ] Counter system (5 attempts)
- [ ] AutoRetainer integration

### Future Phases
- [ ] Configuration options
- [ ] Status display
- [ ] Error handling
- [ ] Logging system

## Installation
1. Download latest release from releases page
2. Extract to `%APPDATA%\XIVLauncher\devPlugins\VERMAXION`
3. Restart XIVLauncher
4. Enable plugin in Dalamud settings

## Usage
1. Enable plugin in Dalamud
2. Click "Start Vermaxion" button
3. Plugin will automatically queue and fail 5 times
4. AutoRetainer will be re-enabled after completion

## Configuration
- Number of attempts (default: 5)
- AutoRetainer toggle behavior
- Queue retry delays

## Requirements
- .NET 10
- Dalamud
- AutoRetainer (for integration)

## Support
Issues and feature requests can be submitted on GitHub Issues.

## License
This plugin is released under the MIT License.
