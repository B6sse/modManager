# Basse's Mod Manager

A simplified version of Frosty Mod Manager that allows users to select from pre-installed, legal mods for competitive play in Star Wars Battlefront 2015.

## Releases

See [Releases](https://github.com/B6sse/modManager/releases) on GitHub for installers and release notes.

## Features

- Simple and clean user interface
- Game path selection on first launch
- Pre-installed mod selection via radio buttons
- Game launch functionality
- No mod import/removal capabilities (locked for simplicity)

## Setup

1. On first launch, select your game executable
2. Select the crosshair mod you want to enable
3. Click "Launch Game" to start the game with selected mods

## Requirements

- Windows 10 or later
- .NET Framework 4.8 or later
- Game executable (Star Wars Battlefront 2015)

## Note

This is a simplified version of Frosty Mod Manager that only allows selection of pre-installed mods. The mod import and removal functionality has been intentionally removed to make sure only approved mods are used. 

The installer automatically installs .NET Framework 4.8 and the Visual C++ Redistributable 2015-2022 (x64) if they are missing, so no manual prerequisite steps should be needed. If the app still does not start, run `vc_redist.x64.exe` and `.NET_Framework_4.8_setup.exe` manually (bundled with the installer in the `Prereqs` folder of the source repository).
