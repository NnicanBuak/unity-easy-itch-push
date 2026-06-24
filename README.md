# Unity Easy Itch Push

<p align="center">
  <img src="Assets/Plugins/EasyItchPush/Documentation~/images/logo.svg" alt="Unity Easy Itch Push logo" width="160">
</p>

Unity Editor plugin for building multiple platform profiles and publishing versioned archives to itch.io with Butler.

## Screenshot

![Unity Easy Itch Push window](Assets/Plugins/EasyItchPush/Documentation~/images/window-screenshot.png)

## What It Does

- Builds all enabled Unity Build Profiles in one run.
- Switches build targets automatically between profiles.
- Writes the configured game version into Player Settings and matching Build Profile settings.
- Creates versioned zip archives per platform after each successful build.
- Includes `CHANGELOG.md` in every generated archive and blocks packaging when the expected version header is missing.
- Validates build outputs before upload, including archive version consistency and HTML5 archive structure.
- Pushes release or test builds to itch.io through Butler.
- Logs all plugin activity to the Unity Console and to daily log files.

## Main Features

### Build All Profiles

The plugin discovers enabled Unity Build Profiles and builds them sequentially. Platform outputs are written into per-channel folders such as:

- `Builds/windows/latest/`
- `Builds/html5/latest/`
- `Builds/linux/latest/`

After each successful build, the plugin creates a versioned archive in the platform folder, for example:

- `Builds/windows/MyGame-Windows-v1.2.3.zip`

### Build Profiles as Source of Truth

The plugin is designed to respect Unity Build Profile platform settings. It updates version data for the active profile without blindly overriding every platform-specific Player Settings value.

### Release and Test Push Modes

The plugin supports separate publishing targets for release and test workflows:

- Different itch usernames, project slugs, and project IDs
- Different remote channel resolution rules
- Shared archive validation before upload

### Push Validation

Before Butler upload, the plugin checks:

- Required itch.io fields are filled
- Archives exist for every required platform
- Archive versions match the configured plugin version
- HTML5 archives contain `index.html` at the archive root
- Partial multi-platform releases are blocked

### Logging

Plugin logs are written to:

- Unity Console
- `logs/EasyItchPush/` daily log files

This helps preserve build diagnostics even when Unity clears console output during platform switches.

## Repository Layout

The plugin is stored as a Unity-ready folder:

```text
Assets/Plugins/EasyItchPush
```

Copy that folder into your Unity project if you want to install the plugin manually.

## Setup

1. Copy `Assets/Plugins/EasyItchPush` into your Unity project.
2. Open `Project Settings > Easy Itch Push`.
3. Configure release and/or test itch.io settings.
4. Set your project version.
5. Make sure your Unity Build Profiles are configured and enabled.
6. Install Butler from `Tools > Easy Itch Push > Install / Upgrade Butler`.
7. Log in through `Tools > Easy Itch Push > Login`.

## Requirements

- Unity project using Build Profiles
- Butler for itch.io uploads
- Valid itch.io credentials and project configuration

## Typical Workflows

### Manual Review

1. Run `Build All Profiles`.
2. Check the generated archives.
3. Run `Push Existing`.

### Full Automation

1. Run `Build All Profiles and Push`.

## Notes

- The plugin can build locally without uploading.
- Pushes use versioned zip archives instead of raw build folders.
- The plugin is focused on editor-side automation for local release workflows.
