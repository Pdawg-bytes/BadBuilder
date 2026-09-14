using BadBuilder.UI;
using BadBuilder.Configuration;
using BadBuilder.Services.Disks;

namespace BadBuilder.Application;

internal static partial class BuilderApp
{
    private static void ConfigureDrive()
    {
        Controls.RenderHeader();

        List<DiskInfo> drives = DiskService.EnumerateDisks();

        if (drives.Count == 0)
        {
            Controls.WriteError("No mounted, ready drives were found.");
            Controls.Pause();
            return;
        }

        static string FormatDriveLabel(DiskInfo drive)
        {
            double sizeGigabytes = drive.Size / 1024d / 1024d / 1024d;
            return $"{drive.Name} ({sizeGigabytes:0.00} GB) - {drive.Type}";
        }

        DiskInfo? targetDisk = Controls.PromptSelection(
            "Choose target drive",
            [..Controls.OptionsWithBack(drives.Select(drive => new MenuOption<DiskInfo?>(drive, FormatDriveLabel(drive))))],
            "[bold white]All data will be lost on this drive.[/] Make sure to select the correct drive."
        );

        if (targetDisk is not null)
            Config.TargetDisk = targetDisk;
        else
            return;
    }

    private static void ConfigureExploit()
    {
        Controls.RenderHeader();

        Config.SelectedExploit = Controls.PromptSelection(
            "Choose exploit",
            [..Controls.OptionsWithBack(ArtifactCatalog.Exploits.Select(pair => new MenuOption<ExploitOption>(pair.Key, pair.Value.DisplayName, pair.Value.Description)))],
            "Executes the console exploit and unlocks the hypervisor, allowing further unsigned code execution."
        );
    }

    private static void ConfigureBootstrap()
    {
        Controls.RenderHeader();

        BootstrapOption selected = Controls.PromptSelection(
            "Choose post-exploit bootstrap",
            [..Controls.OptionsWithBack(ArtifactCatalog.Bootstraps.Select(pair => new MenuOption<BootstrapOption>(pair.Key, pair.Value.DisplayName, pair.Value.Description)))],
            "The payload executed immediately after a successful hypervisor exploit to patch the kernel and initialize homebrew capabilities."
        );

        Config.SelectedBootstrap = selected;

        if (selected != BootstrapOption.XeUnshackle && Config.LaunchHomebrew is not null)
        {
            Config.LaunchHomebrew = null;
            Controls.WriteWarning("The default homebrew launch selection was cleared because automatic launching requires XeUnshackle.");
            Controls.Pause();
        }
    }

    private static void PromptUpdate()
    {
        Controls.RenderHeader();

        int value = Controls.PromptSelection(
            "Choose update option",
            [
                new MenuOption<int>(1, "Update", "Download and install the latest official firmware update."),
                new MenuOption<int>(0, "Cancel", "Cancel the update process."),
                new MenuOption<int>(-1, "Back", "Return to the previous menu without making any changes.")
            ],
            "BadUpdate is only compatible with the latest official firmware version, 2.0.17559.0. If your Xbox 360 is not on that version, then you must apply this update before using any exploits."
        );

        if (value != -1) Config.FirmwareUpdateEnabled = value == 1;
    }
}