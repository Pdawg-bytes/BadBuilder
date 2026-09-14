using BadBuilder.UI;
using BadBuilder.Services;
using BadBuilder.Configuration;

namespace BadBuilder.Application;

internal static partial class BuilderApp
{
    private static void ConfigureHomebrew()
    {
        while (true)
        {
            Controls.RenderHeader();
            Controls.ShowConfigurationSummary(Config);

            string action = Controls.PromptSelection(
                "Homebrew configuration", 
                [
                    new MenuOption<string>("add", "Add homebrew", "Add from archive"),
                    new MenuOption<string>("remove", "Remove homebrew", Config.Homebrew.Count == 0 ? "None" : $"{Config.Homebrew.Count} configured"),
                    new MenuOption<string>("launch", "Set default launch", GetLaunchDisplayName()),
                    new MenuOption<string>("clear-launch", "Clear default launch", "Do not auto-launch a homebrew app"),
                    new MenuOption<string>("back", "Back")
                ]
            );

            switch (action)
            {
                case "add":          AddCustomHomebrew(); break;
                case "remove":       RemoveCustomHomebrew(); break;
                case "launch":       ConfigureLaunchHomebrew(); break;
                case "clear-launch": Config.LaunchHomebrew = null; break;
                default: return;
            }
        }
    }


    private static void AddCustomHomebrew()
    {
        string sourcePath = FileServices.NormalizeUserPath(Controls.PromptText("Enter the homebrew archive path"));

        if (!File.Exists(sourcePath))
        {
            Controls.WriteError("Select an existing archive file.");
            Controls.Pause();
            return;
        }

        string? entryPoint = null;

        bool scan = Controls.PromptSelection(
            "Would you like to scan for an entry point in this archive?",
            [
                new MenuOption<bool>(true, "Yes", "Find the entry point in the archive"),
                new MenuOption<bool>(false, "No", "Install it without automatic launching")
            ]
        );

        if (scan)
        {
            entryPoint = FindEntryPoint(sourcePath);

            if (entryPoint is null)
                return;
        }

        string displayName = Path.GetFileNameWithoutExtension(FileServices.NormalizeUserPath(sourcePath));

        ArtifactDefinition artifact = new(
            $"homebrew-{Guid.NewGuid():N}",
            displayName,
            "Custom homebrew",
            "homebrew",
            null,
            [new InstallOperation(InstallOperationKind.CopyDirectory, $"Apps/{displayName}", ".")],
            ArtifactPriority.Homebrew,
            Path.GetFullPath(sourcePath)
        );

        HomebrewEntry homebrew = new(artifact, SourcePath: artifact.LocalArchivePath, EntryPointRelativePath: entryPoint);

        Config.Homebrew.Add(homebrew);
        Controls.WriteSuccess($"Added {displayName}.");
        Controls.Pause();
    }

    private static string? FindEntryPoint(string sourcePath)
    {
        IReadOnlyList<string> entryPoints;
        try
        {
            var rootEntryPoints = ArchiveService.FindEntryPoints(sourcePath, rootOnly: true);
            entryPoints         = rootEntryPoints.Count == 1 
                ? rootEntryPoints 
                : ArchiveService.FindEntryPoints(sourcePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
        {
            Controls.WriteError($"That file could not be opened as an archive: {ex.Message}");
            Controls.Pause();
            return null;
        }

        if (entryPoints.Count == 0)
        {
            Controls.WriteError("The archive does not contain an .xex entry point.");
            Controls.Pause();
            return null;
        }

        return entryPoints.Count == 1
            ? entryPoints[0]
            : Controls.PromptSelection("Choose the homebrew entry point", [..entryPoints.Select(path => new MenuOption<string>(path, path))]);
    }

    private static void RemoveCustomHomebrew()
    {
        if (Config.Homebrew.Count == 0)
        {
            Controls.WriteWarning("No custom homebrew entries are configured.");
            Controls.Pause();
            return;
        }

        string selectedId = Controls.PromptSelection(
            "Select a homebrew entry to remove", 
            [..Config.Homebrew.Select(homebrew => new MenuOption<string>(homebrew.Artifact.ID, homebrew.Artifact.DisplayName, homebrew.SourcePath ?? "Included package"))]
        );

        HomebrewEntry removed = Config.Homebrew.First(homebrew => homebrew.Artifact.ID == selectedId);
        Config.Homebrew.RemoveAll(homebrew => homebrew.Artifact.ID == selectedId);

        if (Config.LaunchHomebrew?.Artifact.ID == selectedId)
            Config.LaunchHomebrew = null;

        Controls.WriteSuccess($"Removed {removed.Artifact.DisplayName}.");
        Controls.Pause();
    }


    private static void ConfigureLaunchHomebrew()
    {
        if (Config.SelectedBootstrap != BootstrapOption.XeUnshackle)
        {
            Controls.WriteWarning("Automatic homebrew launch requires the XeUnshackle bootstrap.");
            Controls.Pause();
            return;
        }

        HomebrewEntry[] availableHomebrew = [..Config.Homebrew.Where(homebrew => homebrew.EntryPointRelativePath is not null)];
        MenuOption<string>[] options      = [..availableHomebrew
            .Select(homebrew => new MenuOption<string>(homebrew.Artifact.ID, homebrew.Artifact.DisplayName, homebrew.Artifact.Description))];

        if (options.Length == 0)
        {
            Controls.WriteWarning("No homebrew entries with a known entry point are currently available to launch.");
            Controls.Pause();
            return;
        }

        string selectedLaunch = Controls.PromptSelection("Choose default launch program", options);
        Config.LaunchHomebrew = availableHomebrew.FirstOrDefault(h => h.Artifact.ID == selectedLaunch);
    }

    private static string GetLaunchDisplayName() => Config.Homebrew.FirstOrDefault(homebrew => homebrew == Config.LaunchHomebrew)?.Artifact.DisplayName ?? "None";
}