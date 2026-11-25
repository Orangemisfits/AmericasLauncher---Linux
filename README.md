# Americas Launcher

A simple Windows launcher that sets up and starts [XCL](https://github.com/xcl-dev/xcl), a compatibility layer for running Xbox Series X|S (GDK) games on Windows, with a single click.

> This project is intended **only for games you legally own**, unpacked on your own PC. Americas Launcher does **not** include, download, or distribute games, keys, or console SDK files. It is not affiliated with the XCL project, which is developed separately under its own license.

## Features

- Select your game folder and game executable (`Game.exe`)
- One-click **Install / Update**:
  - installs missing requirements with `winget` (Git, Visual Studio C++ Build Tools, Rust)
  - downloads the XCL source with `git` and keeps it up to date
  - builds XCL for you
- One-click game launch
- Runners are defined in `Data/runners.json`, so adding another one requires no code changes

## Requirements

- Windows 10/11 (64-bit) with [winget](https://learn.microsoft.com/windows/package-manager/) (App Installer)
- A D3D12 GPU with Shader Model 6.6 and mesh shaders (required by XCL)
- Several GB of free disk space and an internet connection for the first setup (Visual Studio Build Tools and Rust are large)
- [.NET 8 SDK](https://dotnet.microsoft.com/download) to build the launcher from source, or use a release build

Windows may ask for permission (UAC) while the requirements are installed.

### Windows 10 note

On Windows 10, XCL additionally needs the Microsoft D3D12 Agility SDK files placed next to `xcl.exe`. See the [XCL README](https://github.com/xcl-dev/xcl) for details. The launcher builds XCL to:

```
%LocalAppData%\AmericasLauncher\sources\xcl\target\release\xcl.exe
```

## Building the launcher

```powershell
git clone https://github.com/<your-username>/AmericasLauncher.git
cd AmericasLauncher/src/AmericasLauncher
dotnet run
```

To create a release build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

## Usage

1. Click **Select game folder** and choose the folder that contains your game.
2. Click **Select Game Exe (Game.exe)** and choose the game's executable inside that folder.
3. Click **Install / Update**. The first run installs the requirements and builds XCL, which can take a long time. Watch the log at the bottom.
4. Click **Launch game**.

Data is stored in `%LocalAppData%\AmericasLauncher\` (`config.json` and `sources\`).

## Adding a runner

Add an entry to `src/AmericasLauncher/Data/runners.json`:

```json
{
  "id": "example",
  "name": "Example",
  "gitUrl": "https://github.com/owner/repo.git",
  "dependencies": [
    { "name": "Git", "wingetId": "Git.Git", "check": "command:git" }
  ],
  "buildCommands": [ "cargo build --release" ],
  "exe": "target\\release\\example.exe",
  "launchArgs": "run \"{gameFolder}\"",
  "notes": "Optional text shown after a successful setup."
}
```

| Field | Description |
|---|---|
| `gitUrl` | Repository cloned into `%LocalAppData%\AmericasLauncher\sources\<id>` |
| `dependencies` | winget packages to install if missing. `check` is `command:<exe>` (looked up in PATH) or `vsbuildtools` |
| `buildCommands` | Commands run with `cmd.exe` inside the source folder |
| `exe` | Built executable, relative to the source folder |
| `launchArgs` | Arguments for the executable. `{gameFolder}` and `{gameExe}` are replaced with your selections |

## Roadmap

- [x] Stage 1: Game selection, install, launch
- [x] Stage 2: Dependency check and install via winget
- [ ] Stage 3: Game library and settings page
- [ ] Stage 4: Multiple runners and an installer

## Contributing

Pull requests are welcome. For larger changes, please open an issue first to discuss what you would like to change. By contributing, you agree that your code will be licensed under GPL-2.0.

## Disclaimer

Americas Launcher is not affiliated with the developers of XCL or any other project it can set up. Those projects are subject to their own licenses. Use of pirated or leaked content is not supported.

## License

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
