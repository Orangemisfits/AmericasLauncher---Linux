# Americas Launcher

A simple Windows launcher that downloads and updates Xbox 360 emulators (currently **Xenia Canary**) and starts your game with a single click.

> This project is intended **only for games you legally own**. Americas Launcher does **not** include, download, or distribute games, BIOS files, or firmware.

## Features

- Game file selection (`.iso`, `.xex`, `.zar`)
- Downloads and updates the latest emulator release from GitHub Releases
- One-click game launch
- Emulators are defined in `Data/emulators.json`, so adding a new one requires no code changes

## Requirements

- Windows 10/11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build) or the .NET 8 Desktop Runtime (to run)
- A Vulkan-capable GPU with up-to-date drivers (required by Xenia)

## Building

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

1. Click **Select game...** and choose your game file.
2. Pick an emulator from the list and click **Install / Update**.
3. Click **Launch game**.

Data is stored in `%LocalAppData%\AmericasLauncher\` (`config.json` and `emulators\`).

## Adding a new emulator

Add an entry to `src/AmericasLauncher/Data/emulators.json`:

```json
{
  "id": "example",
  "name": "Example Emulator",
  "repo": "owner/repo",
  "assetPattern": "^example.*windows.*\\.zip$",
  "exe": "example.exe",
  "launchArgs": "\"{gamePath}\""
}
```

Fields:

| Field | Description |
|---|---|
| `repo` | GitHub repository that publishes Releases |
| `assetPattern` | Regex matching the zip file to download |
| `exe` | Executable inside the zip |
| `launchArgs` | Command-line arguments (`{gamePath}` is replaced with the selected game path) |

## Roadmap

- [x] Stage 1: Game selection, emulator download, launch
- [ ] Stage 2: Dependency check and install via winget (VC++ Redistributable, etc.)
- [ ] Stage 3: Game library and settings page
- [ ] Stage 4: Multiple emulators and an installer

## Contributing

Pull requests are welcome. For larger changes, please open an issue first to discuss what you would like to change. By contributing, you agree that your code will be licensed under GPL-2.0.

## Disclaimer

Americas Launcher is not affiliated with the developers of Xenia or any other emulator. Those projects are subject to their own licenses. Use of pirated content is not supported.

## License

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
