# BadBuilder
BadBuilder is a tool for creating BadUpdate/ABadAvatar USB drives for the Xbox 360. It automates the process of formatting the USB drive, downloading required files, extracting them, adding homebrew, and downloading dashboard updates if needed.

## Features
### USB Formatting (Windows & macOS)
- Uses a custom FAT32 formatter that supports large USB drives (≥32GB).
- Ensures compatibility with the Xbox 360.
- Much more stable than the formatter in BadBuilder v1.

> [!NOTE]  
> The formatting feature is supported on **Windows and macOS**. On other operating systems (e.g. Linux), BadBuilder will prompt you to manually format your target disk.

### Automatic File Downloading
- Detects and downloads the latest required files automatically.
- Recognizes previously downloaded files and reuses them by default unless new versions are released.
- Allows specifying custom paths for required files if they can't be downloaded due to network conditions.
> [!IMPORTANT]  
> BadBuilder does not dynamically locate files inside ZIP archives. If your provided archive has a different folder structure than expected, the process will fail abruptly. Ensure your archive matches the expected format if specifying an existing copy.

### File Extraction & Copying
- Extracts all necessary files automatically.
- Prepares the USB drive for the selected exploit by copying all required files.
### Homebrew Support
- Aurora, XeXMenu, and Simple 360 NAND Flasher are included but toggleable.
- Custom homebrew archives may be added via the Homebrew menu.
- Lets you choose a custom `.xex` entry point and make that application the default launch option.
- Automatically searches for the entry point (`.xex`) file within the archive.
- If multiple `.xex` files are found, BadBuilder will prompt you to select the correct one.
- Copies all necessary files.

## How to Use
1. **Launch the executable as Administrator/sudo**. It will open inside of a Terminal window. If the process is not elevated, BadBuilder will refuse to launch.
2. **Navigate the menus**. BadBuilder now uses a configuration system rather than being a fixed series of prompts. You may navigate the menus in any order.
- 2.a. **Target drive:** This menu is where you select the target drive that BadBuilder will format and write files to.
- 2.b. **Exploit:** Choose between BadUpdate and ABadAvatar depending on your preferences.
- 2.c. **Post-exploit bootstrap:** Choose between XeUnshackle and FreeMyXe depending on your preferences.
- 2.d. **Homebrew:** Add new homebrew packages, remove existing ones, and manage the default homebrew to launch via Dashlaunch when `XeUnshackle` is selected as the bootstrap.
- 2.e. **Update Xbox Dashboard:** This menu allows you to change the deployment to apply the `2.0.17559.0` dashboard update required by BadUpdate. **This is only mandatory if your Xbox 360 is not already on this dashboard version.**
- 2.f. **Install:** Begin the deployment process onto your target drive. This is where you will be prompted to format the disk. **Make sure the correct drive is selected.**
> [!CAUTION]
> Formatting a disk means that all data will be lost. Make sure you have selected the right drive before confirming the format. I am not responsible for any data loss.

3. **Begin install**. When you're satisfied with your configuration, you can navigate to the Install menu option, accept the format prompt, and BadBuilder will prepare your USB drive.

### Offline and Local Archives
- BadBuilder uses the download cache when a release cannot be reached.
- If release metadata or a download cannot be resolved, it prompts for a local archive and uses it from its existing location.
- When release metadata includes a SHA-256 hash, BadBuilder compares it with cached, downloaded, and user-supplied archives and redownloads or rejects changed files automatically.
- Archives without a published hash can still be reused, but BadBuilder will warn that their contents are unverified and may cause the install to fail.

## Reporting Issues
If you encounter any problems, please create a new issue with details about your setup and the problem.

### Credits
- **Grimdoomer:** [BadUpdate](https://github.com/grimdoomer/Xbox360BadUpdate)
- **Shutterbug2000:** [ABadAvatar](https://github.com/shutterbug2000/ABadAvatar)
- **InvoxiPlayGames:** [FreeMyXe](https://github.com/FreeMyXe/FreeMyXe)
- **Byrom90:** [XeUnshackle](https://github.com/Byrom90/XeUnshackle)
- **Swizzy:** [Simple 360 NAND Flasher](https://github.com/Swizzy/XDK_Projects)
- **Team XeDEV:** XeXMenu
- **Phoenix:** [Aurora Dashboard](https://phoenix.xboxunity.net/)
