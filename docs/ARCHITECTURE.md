# BadBuilder — Architecture

## Source Layout

```
BadBuilder-1/
├── BadBuilder.sh                          # Standalone Linux Bash implementation
├── BadBuilder/
│   ├── BadBuilder.csproj                  # Cross-platform project file
│   ├── Program.cs                         # Entry point + cancellation setup
│   │
│   ├── Application/
│   │   ├── BuilderApp.cs                  # Main loop + Install orchestration
│   │   ├── BuilderApp.Configuration.cs    # Drive / Exploit / Bootstrap / Update menus
│   │   └── BuilderApp.Homebrew.cs         # Homebrew add / remove / launch menus
│   │
│   ├── Configuration/
│   │   ├── ArtifactCatalog.cs             # Static registry of every downloadable artifact
│   │   ├── BuilderConfig.cs               # Mutable session state
│   │   └── Models.cs                      # Records and enums (ArtifactDefinition, Source, etc.)
│   │
│   ├── Services/
│   │   ├── Disks/
│   │   │   ├── DiskService.cs             # Platform dispatcher (#if OS_WINDOWS / OS_LINUX)
│   │   │   ├── DiskService.Windows.cs     # WMI-based disk ops
│   │   │   └── DiskService.Linux.cs       # lsblk / parted / mount ops
│   │   ├── ArchiveService.cs              # SharpCompress extraction + .xex scanner
│   │   ├── DownloadService.cs             # GitHub / HTTP download pipeline
│   │   ├── FileServices.cs                # Path normalization, SHA-256 hashing
│   │   └── InstallService.cs              # InstallOperation executor
│   │
│   └── UI/
│       └── Controls.cs                    # Spectre.Console menus, progress, summary
└── docs/
    ├── ARCHITECTURE.md                    # This file
    └── FLOWS.md                           # Step-by-step flows
```

---

## Layer Overview

```
┌──────────────────────────────────────────────────────────┐
│  UI Layer — Controls.cs (Spectre.Console)                │
│  Menus, selection prompts, progress bars, summary panel  │
└───────────────────────────┬──────────────────────────────┘
                            │ calls
┌───────────────────────────▼──────────────────────────────┐
│  Application Layer — BuilderApp (partial class, 3 files) │
│  Menu routing, configuration accumulation, install flow  │
└──────────┬───────────────────────────┬───────────────────┘
           │ reads                     │ calls
┌──────────▼──────────┐   ┌────────────▼───────────────────┐
│  Configuration      │   │  Services                      │
│  ArtifactCatalog    │   │  DownloadService               │
│  BuilderConfig      │   │  ArchiveService                │
│  Models             │   │  InstallService                │
└─────────────────────┘   │  DiskService (dispatcher)      │
                          │    ├─ DiskService.Windows.cs   │
                          │    └─ DiskService.Linux.cs     │
                          └────────────────────────────────┘
```

---

## Key Design Patterns

### 1. ArtifactCatalog — single source of truth

Every downloadable item is described by an `ArtifactDefinition` record:

```csharp
record ArtifactDefinition(
    string ID,
    string DisplayName,
    string Description,
    string DestinationRelativePath,
    Source? Source,                        // GitHubReleaseSource or DirectSource
    IReadOnlyList<InstallOperation>?,      // what to do after extraction
    ArtifactPriority Priority,
    string? LocalArchivePath);             // user-supplied local override
```

The `Source` discriminated union decouples *where to fetch* from *what to do with it*:

| Source type | Resolved by |
|---|---|
| `GitHubReleaseSource(Owner, Repo, Tag?, AssetName?)` | Octokit → latest or pinned release |
| `DirectSource(URL)` | HTTP GET with content-length streaming |

### 2. ArtifactPriority — deterministic install order

`GetSelectedArtifacts` always returns artifacts sorted by priority:

```
DashboardUpdate → RockBandBlitz → Exploit → Bootstrap → Homebrew
```

This guarantees the exploit's game data exists before the exploit itself is copied, and the bootstrap is in place before any homebrew that depends on it.

### 3. InstallOperation DSL

Each `ArtifactDefinition` carries a list of typed install operations. The `InstallService` executes them against the USB mount point:

| Operation | Meaning |
|---|---|
| `CopyDirectory(src, dst)` | Recursive copy of `src` inside staging into `dst` on USB |
| `CopyFile(src, dst)` | Single-file copy |
| `WriteFile(dst, contents)` | Create a file from a literal string (e.g. `name.txt`) |
| `RenameFile(src, dst)` | Move a file within the USB (e.g. `default.xex → FreeMyXe.xex`) |

Paths are resolved by `ResolveInside(root, relativePath)`, which:
- Normalises separators on Windows and Linux.
- Resolves the special token `<SUBFOLDER>` to the first subdirectory found at that path — used by XeUnshackle's archive which contains a single top-level folder.
- Performs a path-escape check: any resolved path that is outside the root throws `InvalidOperationException` (Zip-slip protection).

### 4. Download Pipeline — three-stage planner

`DownloadService` runs four sequential stages:

```
ResolveArtifacts   → hit GitHub API / validate direct URL for each artifact
CollectOverrides   → gather local paths (LocalArchivePath field + UI prompts for unresolvable assets)
BuildDownloadPlans → decide action per artifact: Reuse | Use | Download
RunDownloads       → execute Download plans with Spectre.Console progress bars
```

Each plan has one of three actions:

| Action | Condition |
|---|---|
| `Reuse` | Cached archive exists and SHA-256 (or version tag) still matches |
| `Use` | User supplied a local override that passes the hash check |
| `Download` | No valid cache; fetch from resolved URL |

After every download the service writes a `download-manifest.json` alongside the archive:

```json
{
  "VersionTag": "v1.2.3",
  "ArchivePath": "/path/to/file.zip",
  "ExpectedSHA256": "ABC...",
  "SHA256": "ABC...",
  "DownloadedAtUtc": "2025-01-01T00:00:00Z"
}
```

This manifest is the cache key: on the next run, if the archive exists and the manifest's tag or hash still matches the release, it is reused without re-downloading.

### 5. Cross-platform DiskService — compile-time dispatch

The project defines `OS_WINDOWS` and `OS_LINUX` preprocessor symbols via MSBuild:

```xml
<PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Windows'))">
  <DefineConstants>$(DefineConstants);OS_WINDOWS</DefineConstants>
</PropertyGroup>
```

`DiskService.cs` uses `#if OS_WINDOWS / #elif OS_LINUX` blocks, and the wrong-platform files are excluded from compilation:

```xml
<ItemGroup Condition="!$([MSBuild]::IsOSPlatform('Linux'))">
  <Compile Remove="Services\Disks\DiskService.Linux.cs" />
</ItemGroup>
```

Platform implementations:

| Operation | Windows | Linux |
|---|---|---|
| Enumerate disks | WMI `Win32_DiskDrive` query | `lsblk -J -d -b` → `System.Text.Json` |
| Detect removable | WMI `MediaType` | `rm` field or `tran == "usb"` |
| Format FAT32 | DiscUtils `FatFileSystem` (in-process) | `parted mklabel msdos` + `mkfs.fat -F 32` |
| Partition reload | DiscUtils handles it | `partprobe` / `blockdev --rereadpt` + `udevadm settle` |
| Mount point | Drive letter assigned by Windows | `/tmp/badbuilder_<devname>` via `mount` |
| Partition polling | Windows assigns letters automatically | Poll `/sys/class/block/<dev>1` up to 5 s |

---

## Bash Script Architecture (`BadBuilder.sh`)

The Bash script is a **self-contained implementation** of the same pipeline, with no shared code with the C# project. It mirrors the same conceptual model:

| C# concept | Bash equivalent |
|---|---|
| `ArtifactCatalog` + `ArtifactDefinition` | Hardcoded `case` blocks in `install_exploit`, `install_bootstrap`, `install_homebrew_entry` |
| `GitHubReleaseSource` | `gh_release_json owner repo [tag]` → `curl \| jq` |
| `DownloadManifest` | `$DOWNLOAD_DIR/<id>/version.txt` + SHA-256 comparison |
| `InstallOperation.CopyDirectory` | `cp -r src/. dst/` |
| `<SUBFOLDER>` token | `first_subdir()` → `find -mindepth 1 -maxdepth 1 -type d \| sort \| head -1` |
| `BuilderConfig` | Global shell variables (`SELECTED_EXPLOIT`, `MOUNT_POINT`, etc.) |
| Spectre.Console menus | `prompt_menu` + ANSI color codes |
| `msg_info / msg_ok / msg_err` | Redirect to **stderr** so they don't corrupt command-substitution captures |

---

## Data Flow Summary

```
User input (menus)
    │
    ▼
BuilderConfig (target disk, exploit, bootstrap, homebrew list, launch app)
    │
    ├─► ArtifactCatalog.GetSelectedArtifacts(config)
    │        returns: [ArtifactDefinition] sorted by Priority
    │
    ├─► DiskService.FormatFAT32(targetDisk)
    │        returns: mountPoint string
    │
    ├─► DownloadService.DownloadAsync(artifacts, downloadRoot)
    │        returns: DownloadResult { DownloadedCount, [(Artifact, ArchivePath)] }
    │
    ├─► ArchiveService.ExtractAsync(archivePath, stagingRoot)  ← per artifact
    │        returns: stagingPath string
    │
    └─► InstallService.ExecuteAsync(staged, extraOps, mountPoint)
             executes: CopyDirectory / RenameFile / WriteFile per artifact
             writes:   name.txt, info.txt
             updates:  launch.ini (if LaunchHomebrew set)
```
