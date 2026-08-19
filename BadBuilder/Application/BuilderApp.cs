using BadBuilder.Configuration;
using BadBuilder.Services;
using BadBuilder.Services.Disks;
using BadBuilder.UI;
using System.Runtime.InteropServices;

namespace BadBuilder.Application;

internal static partial class BuilderApp
{
    private static readonly BuilderConfig Config = new(ArtifactCatalog.Homebrew);

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        bool running = true;
        while (running && !cancellationToken.IsCancellationRequested)
        {
            Controls.RenderHeader();
            Controls.ShowConfigurationSummary(Config);

            List<MenuOption<RootAction>> options =
            [
                new MenuOption<RootAction>(RootAction.ConfigureDrive, "Target drive"),
                new MenuOption<RootAction>(RootAction.ConfigureExploit, "Exploit"),
                new MenuOption<RootAction>(RootAction.ConfigureBootstrap, "Post-exploit bootstrap"),
                new MenuOption<RootAction>(RootAction.ConfigureHomebrew, "Homebrew"),
                new MenuOption<RootAction>(RootAction.UpdateXbox, "Update Xbox Dashboard"),
                new MenuOption<RootAction>(RootAction.Install, "Install"),
                new MenuOption<RootAction>(RootAction.Exit, "Exit")
            ];

            if (Config.FirmwareUpdateEnabled)
                options.RemoveAll(item => item.Value is not (RootAction.ConfigureDrive or RootAction.UpdateXbox or RootAction.Install or RootAction.Exit));

            switch (Controls.PromptSelection("Select an action", options))
            {
                case RootAction.ConfigureDrive:
                    ConfigureDrive();
                    break;
                case RootAction.ConfigureExploit:
                    ConfigureExploit();
                    break;
                case RootAction.ConfigureBootstrap:
                    ConfigureBootstrap();
                    break;
                case RootAction.ConfigureHomebrew:
                    ConfigureHomebrew();
                    break;
                case RootAction.UpdateXbox:
                    PromptUpdate();
                    break;
                case RootAction.Install:
                    await InstallAsync(cancellationToken);
                    break;
                case RootAction.Exit:
                    running = false;
                    break;
            }
        }
    }

    private static async Task InstallAsync(CancellationToken cancellationToken)
    {
        Controls.RenderHeader();
        Controls.ShowConfigurationSummary(Config);

        try
        {
            if (Config.TargetDisk is null)
                throw new InvalidOperationException("Select a target drive.");

            if (Config.LaunchHomebrew is not null)
            {
                if (Config.SelectedBootstrap != BootstrapOption.XeUnshackle)
                    throw new InvalidOperationException("Automatic homebrew launch requires the XeUnshackle bootstrap.");

                if (Config.LaunchHomebrew.EntryPointRelativePath is null)
                    throw new InvalidOperationException("The selected default homebrew has no valid entry point.");
            }

            bool canFormat = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

            if (!canFormat)
            {
                Controls.WriteWarning("Drive formatting is currently only supported on Windows and macOS. Please format the drive manually to FAT32 before proceeding.");
                Controls.Pause("Press enter after you have formatted the drive.");
                Config.MountPoint = Controls.PromptText("Enter the mount point of your formatted drive (e.g. /media/usb)");
            }
            else
            {
                bool format = Controls.Confirm($"Are you sure you would like to format [bold]{Config.TargetDisk.Name}[/]? All data on this drive will be lost.", false, warning: true);
                Controls.PadLine();

                if (format)
                {
                    Controls.WriteInfo("Formatting drive.");
                    Config.MountPoint = DiskService.FormatFAT32(Config.TargetDisk);
                    Controls.WriteSuccess("Drive formatted.");
                }
                else
                    return;
            }

            if (Config.MountPoint is null) throw new Exception("Format did not remount the drive, installation cannot continue.");

            var artifacts       = ArtifactCatalog.GetSelectedArtifacts(Config);
            string workRoot     = Path.Combine(AppContext.BaseDirectory, "Work");
            string downloadRoot = Path.Combine(workRoot, "Downloads");
            string stagingRoot  = Path.Combine(workRoot, "Staging");

            Directory.CreateDirectory(downloadRoot);
            Controls.PadLine();
            DownloadResult download = await DownloadService.DownloadAsync(artifacts, downloadRoot, cancellationToken);

            if (download.DownloadedCount > 0) Controls.WriteSuccess($"[bold]{download.DownloadedCount}[/] download(s) completed.");
            else                              Controls.WriteSuccess("All downloads are already up to date.");

            Controls.PadLine();
            Controls.WriteInfo("Extracting files.");
            Directory.CreateDirectory(stagingRoot);

            var staged = await Task.WhenAll(download.Artifacts.Select(async item =>
                (item.Artifact, StagingPath: await ArchiveService.ExtractAsync(item.ArchivePath, stagingRoot, cancellationToken))));

            Controls.WriteSuccess("Files extracted.");
            Controls.PadLine();
            Controls.WriteInfo("Copying files.");

            string configText = $"Configuration:\n{Config}";

            List<InstallOperation> extraOperations =
            [
                new(InstallOperationKind.WriteFile, "name.txt", Contents: "USB Storage Device"),
                new(InstallOperationKind.WriteFile, "info.txt", Contents: "This drive was created with BadBuilder by Pdawg.\nFind more info here: https://github.com/Pdawg-bytes/BadBuilder" + $"\n\n{configText}")
            ];

            await InstallService.ExecuteAsync(staged, extraOperations, Config.MountPoint, cancellationToken);

            if (Config.LaunchHomebrew is not null)
            {
                string iniPath = Path.Combine(Config.MountPoint, "launch.ini");

                if (File.Exists(iniPath))
                {
                    List<string> lines    = [..File.ReadAllLines(iniPath)];
                    string newDefaultPath = $"Usb:\\Apps\\{Config.LaunchHomebrew.Artifact.DisplayName}\\{Config.LaunchHomebrew.EntryPointRelativePath}";

                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Trim().StartsWith("Default", StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = $"Default = {newDefaultPath}";
                            break;
                        }
                    }

                    File.WriteAllLines(iniPath, lines);
                }
                else
                {
                    Controls.WriteWarning("launch.ini file not found at the specified mount point. Dashlaunch will not automatically launch your selected homebrew.");
                    Controls.Pause();
                }
            }

            Controls.WriteSuccess("Your USB drive is ready for use.");

            Controls.Pause("Press enter to exit...");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Controls.WriteError($"Install failed: {ex.Message}");
            Controls.Pause();
        }
    }
}

internal enum RootAction
{
    ConfigureDrive,
    ConfigureExploit,
    ConfigureBootstrap,
    ConfigureHomebrew,
    UpdateXbox,
    Install,
    Exit,
}