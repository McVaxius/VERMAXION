# How to Import Dalamud Plugins

## Prerequisites
1. **XIVLauncher** - Latest version installed
2. **Dalamud** - Enabled in XIVLauncher settings
3. **.NET 10** - Required for modern plugins

## Installation Methods

### Method 1: Manual Installation (Recommended for Development)

#### Step 1: Locate Plugin Directory
Navigate to your Dalamud plugins folder:
```
%APPDATA%\XIVLauncher\devPlugins\
```
Or open Run dialog and enter:
```
%localappdata%\XIVLauncher\addon\hooks\dev\
```

#### Step 2: Create Plugin Folder
1. Create a new folder named `VERMAXION` in the plugins directory
2. Ensure the folder name matches exactly (case-sensitive)

#### Step 3: Extract Plugin Files
1. Download or copy the plugin files to the `VERMAXION` folder
2. Verify the folder structure:
```
VERMAXION/
├── VERMAXION.dll
├── VERMAXION.json
├── VERMAXION.pdb (optional)
└── README.md
```

#### Step 4: Restart Game
1. Completely close FFXIV
2. Restart XIVLauncher
3. Launch FFXIV

### Method 2: In-Game Plugin Installer

#### Step 1: Open Plugin Installer
1. Type `/xlplugins` in chat
2. Click "Plugin Installer" button
3. Search for "Vermaxion"

#### Step 2: Install Plugin
1. Click "Install" on the plugin
2. Wait for installation to complete
3. Restart the game

### Method 3: Git Repository (For Developers)

#### Step 1: Clone Repository
```bash
cd %APPDATA%\XIVLauncher\devPlugins\
git clone <repository-url> VERMAXION
```

#### Step 2: Build Plugin
1. Open the solution in Visual Studio
2. Build in Release configuration
3. Copy output to plugins folder

## Enabling the Plugin

### Step 1: Open Dalamud Settings
1. Type `/xlsettings` in chat
2. Or click the Dalamud icon in the main menu

### Step 2: Enable Plugin
1. Find "Vermaxion" in the plugin list
2. Toggle the switch to enable
3. Configure any settings as needed

### Step 3: Verify Installation
1. Look for the Vermaxion UI window
2. Check that the plugin appears in `/xlplugins` list
3. Verify plugin functions correctly

## Troubleshooting

### Plugin Not Showing Up
**Possible Causes:**
- Incorrect folder structure
- Missing required files
- .NET version mismatch
- Dalamud version incompatibility

**Solutions:**
1. Verify folder structure matches exactly
2. Check that `VERMAXION.json` exists and is valid
3. Ensure .NET 10 is installed
4. Update Dalamud to latest version

### Plugin Crashes on Load
**Possible Causes:**
- Missing dependencies
- Corrupted installation
- API version mismatch

**Solutions:**
1. Check Dalamud log for errors
2. Reinstall the plugin completely
3. Verify all dependencies are installed
4. Report issue to plugin developer

### Plugin Shows but Doesn't Work
**Possible Causes:**
- Incorrect permissions
- Game version incompatibility
- Conflicts with other plugins

**Solutions:**
1. Check plugin permissions in settings
2. Verify game version compatibility
3. Try disabling other plugins temporarily
4. Check plugin configuration

## Development Setup

### Prerequisites
- Visual Studio 2022 or later
- .NET 10 SDK
- Dalamud.NET.Sdk

### Steps
1. Clone the repository
2. Open `VERMAXION.sln` in Visual Studio
3. Restore NuGet packages
4. Build in Debug configuration
5. Copy output to plugins folder for testing

### Debugging
1. Attach debugger to FFXIV process
2. Use `Debug.WriteLine()` for logging
3. Check Dalamud console (`/xllog`)
4. Monitor plugin performance

## Plugin Files Explained

### Required Files
- **VERMAXION.dll** - Main plugin assembly
- **VERMAXION.json** - Plugin metadata and dependencies

### Optional Files
- **VERMAXION.pdb** - Debug symbols
- **README.md** - Plugin documentation
- **CHANGELOG.md** - Version history
- **LICENSE** - License information

### Configuration Files
- **configuration.json** - User settings (auto-generated)
- **logs/** - Debug logs (auto-generated)

## Best Practices

### Installation
- Always backup existing plugins before updating
- Read plugin documentation before installation
- Check compatibility with your game version

### Development
- Test in a clean environment
- Use version control for changes
- Document all API changes
- Provide clear installation instructions

### Troubleshooting
- Check logs first
- Reproduce the issue consistently
- Provide detailed bug reports
- Include system information

## Getting Help

### Resources
- [Dalamud Discord](https://discord.gg/dalamud)
- [FFXIV Plugin Development](https://github.com/goatcorp/Dalamud)
- [Sample Plugin](https://github.com/goatcorp/SamplePlugin)

### Support Channels
- Plugin GitHub Issues
- Dalamud Discord #plugin-development
- XIVLauncher Discord #support

## Security Considerations

### Safe Practices
- Only download from trusted sources
- Verify plugin signatures when possible
- Read plugin code if you have concerns
- Keep plugins updated

### Warning Signs
- Requests for account credentials
- Unusual file permissions
- Suspicious network activity
- Poorly written code

## Updates and Maintenance

### Updating Plugins
1. Check for updates regularly
2. Backup configuration before updating
3. Test after updating
4. Report any issues

### Plugin Removal
1. Disable plugin in settings
2. Close the game completely
3. Delete plugin folder
4. Restart game

## Conclusion
Following this guide should help you successfully install and use the Vermaxion plugin. If you encounter any issues, please refer to the troubleshooting section or seek help from the provided support channels.
