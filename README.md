# BadBuilder

> Xbox 360 BadUpdate / ABadAvatar USB drive builder — Windows & Linux

BadBuilder automates every step of preparing a bootable USB drive for Xbox 360 kernel exploits: formatting, downloading the right files, verifying their integrity, extracting them, and placing everything in the structure each exploit expects.

---

## Features

| Feature | Windows | Linux (C#) | Linux (`BadBuilder.sh`) |
|---|:---:|:---:|:---:|
| FAT32 format | ✅ DiscUtils | ✅ parted + mkfs.fat | ✅ parted + mkfs.fat |
| Disk enumeration | ✅ WMI | ✅ lsblk | ✅ lsblk |
| GitHub artifact resolution | ✅ | ✅ | ✅ curl + jq |
| SHA-256 verification | ✅ | ✅ | ✅ sha256sum |
| Download cache | ✅ manifest | ✅ manifest | ✅ version + hash |
| Archive extraction | ✅ SharpCompress | ✅ SharpCompress | ✅ unzip/7z/unrar |
| BadUpdate exploit | ✅ | ✅ | ✅ |
| ABadAvatar exploit | ✅ | ✅ | ✅ |
| ABadAvatar 1.3 | ✅ | ✅ | ✅ |
| XeUnshackle bootstrap | ✅ | ✅ | ✅ |
| FreeMyXe bootstrap | ✅ | ✅ | ✅ |
| Built-in homebrew (Aurora, XeXMenu, Flasher) | ✅ | ✅ | ✅ |
| Custom homebrew archives | ✅ | ✅ | ✅ |
| Dashlaunch auto-launch | ✅ | ✅ | ✅ |
| Dashboard update (2.0.17559.0) | ✅ | ✅ | ✅ |

---

## Quick Start

### Requirements

Root / Administrator access is required on all platforms.

### Windows

Download the latest release from the [Releases](../../releases) page and run the `.exe` as Administrator.

### Linux — Bash script (no .NET required)

```bash
# Install dependencies (Debian/Ubuntu)
sudo apt install util-linux dosfstools parted curl jq unzip p7zip-full unrar

# Fedora/RHEL
sudo dnf install util-linux dosfstools parted curl jq unzip p7zip p7zip-plugins unar

# Run
chmod +x BadBuilder.sh
sudo ./BadBuilder.sh
```

### Linux — C# native binary

```bash
# Requires .NET 10 SDK
dotnet publish BadBuilder/BadBuilder.csproj -r linux-x64 -c Release --self-contained
sudo ./BadBuilder/bin/Release/net10.0/linux-x64/publish/BadBuilder
```

---

## How to Use

BadBuilder uses a **configuration-then-install** model. You can navigate the menus in any order, review your choices in the summary panel, and only commit when you're ready.

1. **Launch as root / Administrator.**
2. **Configure each option** via the numbered menu:
   - **Target drive** — select the USB drive. All its data will be erased.
   - **Exploit** — choose BadUpdate or ABadAvatar (see [Exploit Reference](#exploit-reference)).
   - **Post-exploit bootstrap** — XeUnshackle (recommended) or FreeMyXe.
   - **Homebrew** — toggle built-in packages, add custom archives, set the Dashlaunch auto-launch target.
   - **Update Xbox Dashboard** — enable only if your console is not already on firmware `2.0.17559.0`.
3. **Select Install** — confirm the format warning and BadBuilder will handle the rest.

> ⚠️ **Formatting erases all data on the drive.** Double-check the selected drive before confirming.

### Exploit Reference

| Exploit | Method | Notes |
|---|---|---|
| BadUpdate | Rock Band Blitz savedata | Original exploit by grimdoomer |
| ABadAvatar | Avatar system | By shutterbug2000 |
| ABadAvatar 1.3 | Avatar system | Unofficial fork by bibarub, improved speed |

### Bootstrap Reference

| Bootstrap | What it installs | Auto-launch support |
|---|---|---|
| XeUnshackle | Dashlaunch, plugins, high compatibility | ✅ via `launch.ini` |
| FreeMyXe | Minimal patches, XeLL, LibXenon | ❌ |

### Offline and local archives

- BadBuilder uses the download cache when a release cannot be reached online.
- If an asset cannot be resolved, it prompts for a local archive path.
- When the release body contains a `sha256:` hash, BadBuilder compares it to every cached, downloaded, and user-supplied file, and rejects any mismatch.

---

## Architecture

BadBuilder is structured in four layers. See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full breakdown.

```
UI Layer          →  Spectre.Console TUI / ANSI menus
Application Layer →  BuilderApp (menu orchestration, install coordination)
Configuration     →  ArtifactCatalog + BuilderConfig + Models
Services          →  Download • Archive • Install • DiskService (platform-specific)
```

For install and download pipeline flows, see [`docs/FLOWS.md`](docs/FLOWS.md).

---

## Building

```bash
# Debug
dotnet build BadBuilder/BadBuilder.csproj

# Self-contained Windows x64 release
dotnet publish BadBuilder/BadBuilder.csproj -r win-x64 -c Release --self-contained

# Self-contained Linux x64 release
dotnet publish BadBuilder/BadBuilder.csproj -r linux-x64 -c Release --self-contained
```

### Dependencies

| Package | Purpose |
|---|---|
| `Spectre.Console` | TUI menus, progress bars, tables |
| `LTRData.DiscUtils.*` | Cross-platform FAT32 formatting |
| `SharpCompress` | zip / 7z / rar archive extraction |
| `Octokit` | GitHub Releases API client |
| `System.Management` | Windows WMI disk enumeration (Windows only) |

---

## Credits

- **grimdoomer** — [BadUpdate](https://github.com/grimdoomer/Xbox360BadUpdate)
- **shutterbug2000** — [ABadAvatar](https://github.com/shutterbug2000/ABadAvatar)
- **bibarub** — [ABadAvatar 1.3](https://github.com/bibarub/Xbox360BadUpdate)
- **InvoxiPlayGames** — [FreeMyXe](https://github.com/FreeMyXe/FreeMyXe)
- **Byrom90** — [XeUnshackle](https://github.com/Byrom90/XeUnshackle)
- **Swizzy** — [Simple 360 NAND Flasher](https://github.com/Swizzy/XDK_Projects)
- **Team XeDEV** — XeXMenu
- **Phoenix** — [Aurora Dashboard](https://phoenix.xboxunity.net/)
- **Lopobia Faw** — Linux port

---

## License

See [LICENSE](LICENSE).
