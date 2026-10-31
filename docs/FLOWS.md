# BadBuilder — Flows

## 1. Main Install Flow

This is the end-to-end happy path when the user selects **Install**.

```
User confirms install
        │
        ▼
[Validate config]
  ─ TargetDisk must be set
  ─ LaunchHomebrew requires XeUnshackle bootstrap
  ─ LaunchHomebrew.EntryPointRelativePath must not be null
        │
        ▼
[Format FAT32]
  DiskService.FormatFAT32(TargetDisk)
  ─ Windows: DiscUtils writes FAT32 in-process, assigns drive letter
  ─ Linux:   parted mklabel msdos → mkpart primary fat32 1MiB 100%
             mkfs.fat -F 32 -n BADUPDATE <part>
             mount <part> /tmp/badbuilder_<devname>
  → sets MountPoint
        │
        ▼
[Resolve artifacts]
  ArtifactCatalog.GetSelectedArtifacts(config)
  ─ FirmwareUpdate=true  → [DashboardUpdate]
  ─ FirmwareUpdate=false → [Exploit, Bootstrap, (RockBandBlitz if BadUpdate), ...Homebrew]
  → sorted by ArtifactPriority
        │
        ▼
[Download pipeline]  (see §2)
  DownloadService.DownloadAsync(artifacts, downloadRoot)
  → DownloadResult { artifacts with local archive paths }
        │
        ▼
[Extract]
  ArchiveService.ExtractAsync(archivePath, stagingRoot)  ← parallel via Task.WhenAll
  → stagingPath per artifact
        │
        ▼
[Install]
  InstallService.ExecuteAsync(staged, extraOps, mountPoint)
  ─ execute each artifact's InstallOperations in priority order
  ─ execute extra operations: WriteFile("name.txt"), WriteFile("info.txt")
        │
        ▼
[Update launch.ini]  (if LaunchHomebrew != null)
  ─ find launch.ini at mountPoint root
  ─ replace or append:  Default = Usb:\Apps\<Name>\<EntryPoint>
        │
        ▼
[Sync / flush]
  File system sync → USB is safe to eject
        │
        ▼
 ✅ "USB drive is ready"
```

---

## 2. Download Pipeline

```
DownloadAsync(artifacts)
        │
        ▼
[ResolveArtifactsAsync]
  for each artifact:
    GitHubReleaseSource → Octokit.GetLatest or .Get(tag)
                          → asset: first non-checksum file, or named asset
                          → ArtifactReference { VersionTag, Name, DownloadUrl, SHA256? }
    DirectSource        → validate URL, derive filename
                          → ArtifactReference { "direct", filename, URL, null }
    Source = null       → ResolvedArtifact { Release = null }  (local archive only)
  on HttpRequestException / InvalidOperationException:
    → warn user, continue with Release = null
        │
        ▼
[CollectLocalOverridesAsync]
  ─ artifacts with LocalArchivePath set   → add to overrides map
  ─ artifacts with Release = null and no cache → PromptForUnavailableAssetsAsync
      → Controls.PromptArchivePathOverrides (interactive table)
      → user can supply local path; required artifacts must have one
        │
        ▼
[BuildDownloadPlansAsync]
  for each resolved artifact:
    read download-manifest.json (if exists)

    Case A — Release = null:
      have cached archive + manifest → Reuse
      no cache + override provided   → Use(overridePath)
      nothing available              → skip (non-required) or error (required)

    Case B — Release resolved:
      override provided and hash matches → Use(overridePath)
      cached file + (SHA matches OR tag matches manifest) → Reuse
      otherwise → Download(release.DownloadUrl → archivePath)
        │
        ▼
[RunDownloadsAsync]
  filter plans where Action == Download
  run each with Spectre.Console progress bar (streaming, 80 KB buffer)
        │
        ▼
[ExecutePlanAsync] (per Download plan)
  download file → compute SHA-256 → EnsureHashMatches → write manifest
        │
        ▼
[BuildResult]
  → DownloadResult { DownloadedCount, [(Artifact, ArchivePath)] }
```

---

## 3. Install Operations — per artifact

Each `ArtifactDefinition` carries `InstallOperations`. `InstallService.ExecuteAsync` processes them in the order given.

### BadUpdate exploit
```
CopyDirectory(".", "Rock Band Blitz")
  → copy everything from staging root into <USB>/Rock Band Blitz/
```

### ABadAvatar / ABadAvatar 1.3
```
CopyDirectory(".", ".")
  → copy everything from staging root into <USB>/
```

### XeUnshackle bootstrap
```
CopyDirectory(".", "<SUBFOLDER>/.")
  <SUBFOLDER> resolved at runtime: first subdirectory in staging
  → copy <staging>/<first-subdir>/* into <USB>/
  installs: launch.ini, Dashlaunch, plugins, default.xex (XeUnshackle payload)
```

### FreeMyXe bootstrap
```
CopyDirectory("BadUpdatePayload", ".")
  → copy <staging>/BadUpdatePayload/* into <USB>/

RenameFile("BadUpdatePayload/default.xex", "BadUpdatePayload/FreeMyXe.xex")
  → rename inside USB
```

### Aurora homebrew
```
CopyDirectory("Apps/Aurora", ".")
  → copy <staging>/Apps/Aurora/* into <USB>/
  (archive already has the Apps/Aurora/ structure)
result: <USB>/Aurora.xex, <USB>/skin/, etc.

Wait — looking at the ArtifactCatalog more carefully:
  source = Aurora.rar  (contains an Apps/Aurora/ subtree)
  InstallOperation: CopyDirectory("Apps/Aurora", ".")
  → copies <staging>/Apps/Aurora/* → <USB>/
  final path: <USB>/Aurora.xex, <USB>/DATA/, etc.
```

### XeXMenu homebrew
```
CopyDirectory(".", ".")
  → copies everything from staging into <USB>/
  final path: <USB>/default.xex, etc.
```

### Simple 360 NAND Flasher
```
CopyDirectory("Apps/Simple 360 NAND Flasher", "Simple 360 NAND Flasher")
  → copies <staging>/Apps/Simple 360 NAND Flasher/* into <USB>/Simple 360 NAND Flasher/
  final path: <USB>/Simple 360 NAND Flasher/Default.xex, etc.
```

### Custom homebrew
```
CopyDirectory($"Apps/{displayName}", ".")
  → user's archive is extracted to staging
  → everything copied into <USB>/
  → entry point configurable by user
```

### Rock Band Blitz game data
```
CopyDirectory(".", ".")
  → copies GameData.zip contents (game save data) into <USB>/
  required when exploit = BadUpdate
```

---

## 4. Resulting USB Structure

### Exploit: BadUpdate + Bootstrap: XeUnshackle + all homebrew

```
USB:/
├── Rock Band Blitz/           ← BadUpdate exploit savedata
│   └── ...
├── launch.ini                 ← XeUnshackle / Dashlaunch config
├── default.xex                ← XeUnshackle payload
├── plugins/                   ← Dashlaunch plugins
├── Aurora.xex                 ← Aurora (if enabled)
├── DATA/                      ← Aurora data
├── default.xex                ← XeXMenu (overwrites if both selected — prefer disabling one)
├── Simple 360 NAND Flasher/   ← Flasher
│   └── Default.xex
├── name.txt                   ← "USB Storage Device"
└── info.txt                   ← build info + config summary
```

> **Note:** Both XeUnshackle and XeXMenu place a `default.xex` at the USB root. When both are selected, XeXMenu's copy overwrites XeUnshackle's. This is generally not an issue when XeUnshackle is the bootstrap because Dashlaunch (`launch.ini`) controls what is launched — but disabling XeXMenu if you want XeUnshackle's payload to remain accessible directly is recommended.

### Exploit: ABadAvatar + Bootstrap: FreeMyXe

```
USB:/
├── <ABadAvatar files>/        ← avatar exploit files at USB root
├── BadUpdatePayload/
│   └── FreeMyXe.xex           ← FreeMyXe payload (renamed from default.xex)
├── name.txt
└── info.txt
```

### Firmware update mode (FirmwareUpdateEnabled = true)

```
USB:/
├── $SystemUpdate/             ← Microsoft update files
│   └── ...
├── name.txt
└── info.txt
```

---

## 5. Custom Homebrew Flow

```
User: "Add homebrew"
        │
        ▼
[Prompt archive path]
  ─ validate file exists
        │
        ▼
[Scan for entry point?]
  Yes → ArchiveService.FindEntryPoints(path, rootOnly: true)
        if single root .xex → use it directly
        if multiple .xex files → ArchiveService.FindEntryPoints(path)
          → Controls.PromptSelection (user picks the .xex)
        if zero .xex files → error, abort
  No  → entryPoint = null (cannot be set as auto-launch target)
        │
        ▼
[Create ArtifactDefinition]
  ID = "homebrew-{Guid}"
  Source = null (local archive, no download)
  LocalArchivePath = absolute path
  Operations = [ CopyDirectory("Apps/{displayName}", ".") ]
        │
        ▼
[Add HomebrewEntry to BuilderConfig.Homebrew]
  ─ visible in summary panel
  ─ installed during next Install run
```

---

## 6. Cross-Platform Disk Dispatch

```
DiskService.EnumerateDisks()
        │
  ┌─────┴──────┐
  │ #if OS_WINDOWS        #elif OS_LINUX
  ▼             ▼
WMI query     lsblk -J -d -b
Win32_DiskDrive  -o NAME,SIZE,TYPE,TRAN,MODEL,RM
  │             │
  ▼             ▼
List<DiskInfo>  parse JSON with System.Text.Json
                filter: type=="disk", not loop/sr/fd
                removable: rm==true|1 or tran=="usb"


DiskService.FormatFAT32(disk)
        │
  ┌─────┴──────┐
  │ #if OS_WINDOWS        #elif OS_LINUX
  ▼             ▼
DiscUtils       umount_partitions(dev)
FatFileSystem   parted -s dev mklabel msdos
.Format(stream) parted -s dev mkpart primary fat32 1MiB 100%
                partprobe / blockdev --rereadpt
                udevadm settle (or sleep 2)
                poll /sys/class/block/<dev>1 up to 5s
                mkfs.fat -F 32 -n BADUPDATE <part>
                mount <part> /tmp/badbuilder_<devname>
                → sets MountPoint
```
