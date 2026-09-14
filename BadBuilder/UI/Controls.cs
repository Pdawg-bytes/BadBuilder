using Spectre.Console;
using BadBuilder.Configuration;

namespace BadBuilder.UI;

internal static class AppTheme
{
    internal static readonly Style OrangeStyle      = new(new Color(255, 114, 0));
    internal static readonly Style LightOrangeStyle = new(new Color(255, 172, 77));
    internal static readonly Style PeachStyle       = new(new Color(255, 216, 153));
    internal static readonly Style GreenStyle       = new(new Color(118, 185, 0));
}

internal static class Controls
{
    private static string Escape(string text) => Markup.Escape(text);
    private static string ToMarkupColor(Color? color) => color is null ? "white" : $"rgb({color.Value.R},{color.Value.G},{color.Value.B})";


    internal static void RenderHeader()
    {
        AnsiConsole.Clear();
        AnsiConsole.Markup(
        """
        [#4D8C00]██████╗  █████╗ ██████╗ ██████╗ ██╗   ██╗██╗██╗     ██████╗ ███████╗██████╗ [/]
        [#65A800]██╔══██╗██╔══██╗██╔══██╗██╔══██╗██║   ██║██║██║     ██╔══██╗██╔════╝██╔══██╗[/]
        [#76B900]██████╔╝███████║██║  ██║██████╔╝██║   ██║██║██║     ██║  ██║█████╗  ██████╔╝[/]
        [#A1CF3E]██╔══██╗██╔══██║██║  ██║██╔══██╗██║   ██║██║██║     ██║  ██║██╔══╝  ██╔══██╗[/]
        [#CCE388]██████╔╝██║  ██║██████╔╝██████╔╝╚██████╔╝██║███████╗██████╔╝███████╗██║  ██║[/]
        [#CCE388]╚═════╝ ╚═╝  ╚═╝╚═════╝ ╚═════╝  ╚═════╝ ╚═╝╚══════╝╚═════╝ ╚══════╝╚═╝  ╚═╝[/]

        [#76B900]──────────────────────────────────────────────────────────────────────v2.1.0[/]
        ───────────────────────Xbox 360 [#FF7200]BadUpdate[/] USB Builder───────────────────────
                                    [#848589]Created by Pdawg[/]
        [#76B900]────────────────────────────────────────────────────────────────────────────[/]

        """);
        AnsiConsole.WriteLine();
    }

    internal static void PadLine() => AnsiConsole.WriteLine();

    internal static void WriteInfo(string message)    => AnsiConsole.MarkupLine($"[yellow]{Escape("[*]")}[/] {message}");
    internal static void WriteWarning(string message) => AnsiConsole.MarkupLine($"[{ToMarkupColor(AppTheme.LightOrangeStyle.Foreground)}]{Escape("[!]")}[/] {message}");
    internal static void WriteError(string message)   => AnsiConsole.MarkupLine($"[red]{Escape("[-]")}[/] {message}");
    internal static void WriteSuccess(string message) => AnsiConsole.MarkupLine($"[{ToMarkupColor(AppTheme.GreenStyle.Foreground)}]{Escape("[+]")}[/] {message}");


    internal static IReadOnlyList<MenuOption<T?>> OptionsWithBack<T>(IEnumerable<MenuOption<T?>> options) => [..options.Append(new MenuOption<T?>(default, "Back"))];


    internal static T PromptSelection<T>(string title, IReadOnlyList<MenuOption<T>> options, string? details = null) => AnsiConsole.Prompt(
        new SelectionPrompt<MenuOption<T>>()
            .Title($"[{ToMarkupColor(AppTheme.OrangeStyle.Foreground)}]{Escape(title)}[/]" +
                   $"{(!string.IsNullOrWhiteSpace(details) ? $"\n[gray]{details}[/]" : "")}")
            .PageSize(10)
            .HighlightStyle(AppTheme.GreenStyle)
            .UseConverter(option =>
            {
                string label = Escape(option.Label);

                if (string.IsNullOrWhiteSpace(option.Description))
                    return label;

                return $"{label} [gray]- {Escape(option.Description)}[/]";
            })
            .AddChoices(options)
    ).Value;

    internal static IReadOnlyList<T> PromptMultiSelection<T>(string title, IReadOnlyList<MenuOption<T>> options, IReadOnlyCollection<T> selectedValues, string? details = null)
    {
        var prompt = new MultiSelectionPrompt<MenuOption<T>>()
            .Title($"[{ToMarkupColor(AppTheme.OrangeStyle.Foreground)}]{Escape(title)}[/]" +
                   $"{(!string.IsNullOrWhiteSpace(details) ? $"\n[gray]{details}[/]" : "")}")
            .PageSize(10)
            .NotRequired()
            .HighlightStyle(AppTheme.GreenStyle)
            .InstructionsText("[gray](Use [white]<space>[/] to toggle, [white]<enter>[/] to confirm)[/]")
            .UseConverter(option =>
            {
                string label = Escape(option.Label);

                if (string.IsNullOrWhiteSpace(option.Description))
                    return label;

                return $"{label} [gray]- {Escape(option.Description)}[/]";
            })
            .AddChoices(options);

        foreach (var option in options.Where(option => selectedValues.Contains(option.Value)))
            prompt.Select(option);

        return [..AnsiConsole.Prompt(prompt).Select(option => option.Value)];
    }

    internal static bool Confirm(string title, bool defaultValue = true, bool warning = false) => AnsiConsole.Prompt(
        new TextPrompt<bool>($"{(warning ? $"[{ToMarkupColor(AppTheme.OrangeStyle.Foreground)} bold]WARNING:[/] {title}" : title)}")
            .AddChoice(true)
            .AddChoice(false)
            .DefaultValue(defaultValue)
            .ChoicesStyle(AppTheme.GreenStyle.Foreground)
            .DefaultValueStyle(AppTheme.OrangeStyle.Foreground)
            .WithConverter(choice => choice ? "y" : "n")
    );

    internal static string PromptText(string title, string? defaultValue = null)
    {
        var prompt = new TextPrompt<string>($"[{ToMarkupColor(AppTheme.OrangeStyle.Foreground)}]{Escape(title)}[/]")
            .AllowEmpty();

        if (defaultValue is not null)
            prompt.DefaultValue(defaultValue);

        return AnsiConsole.Prompt(prompt).Trim().Trim('"', '\'');
    }

    internal static void Pause(string message = "Press enter to continue...")
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup($"[gray]{Escape(message)}[/]");
        Console.ReadLine();
    }

    internal static Task RunProgressAsync(IReadOnlyList<ProgressOperation> operations, CancellationToken cancellationToken)
    {
        return AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn().FinishedStyle(AppTheme.GreenStyle.Foreground).CompletedStyle(AppTheme.LightOrangeStyle.Foreground),
                new PercentageColumn().CompletedStyle(AppTheme.GreenStyle.Foreground),
                new RemainingTimeColumn().Style(Color.Gray),
                new TransferSpeedColumn()
            )
            .StartAsync(async context =>
            {
                var tasks = operations.Select(operation =>
                {
                    ProgressTask task = context.AddTask(Escape(operation.Description), autoStart: true);
                    return operation.Action(task, cancellationToken);
                });

                await Task.WhenAll(tasks);
            });
    }


    internal static void ShowConfigurationSummary(BuilderConfig config)
    {
        string green = ToMarkupColor(AppTheme.GreenStyle.Foreground);
        Table table = new Table().Border(TableBorder.Rounded);

        table.AddColumn(new TableColumn(new Markup($"[bold {green}]Section[/]")).LeftAligned());
        table.AddColumn(new TableColumn(new Markup($"[bold {green}]Selection[/]")).LeftAligned());

        table.AddRow("Drive", $"[gray]{Escape(config.TargetDisk?.Name ?? "None selected")}[/]");

        if (config.FirmwareUpdateEnabled)
        {
            table.AddRow("Update", "[gray]Update to 2.0.17559.0[/]");
        }
        else
        {
            table.AddRow("Exploit", $"[gray]{Escape(config.SelectedExploit.ToString())}[/]");
            table.AddRow("Bootstrap", $"[gray]{Escape(config.SelectedBootstrap.ToString())}[/]");

            string[] homebrew = [..config.Homebrew
            .Select(homebrew => homebrew.Artifact.ID == config.LaunchHomebrew?.Artifact.ID
                ? $"[{green}]{Escape(homebrew.Artifact.DisplayName)}[/] [gray](launch)[/]"
                : $"[gray]{Escape(homebrew.Artifact.DisplayName)}[/]")];

            table.AddRow("Homebrew", string.Join(", ", homebrew.DefaultIfEmpty("[gray]None[/]")));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    internal static Dictionary<string, string> PromptArchivePathOverrides(string title, IReadOnlyList<ArchivePathEntry> entries, string? details = null)
    {
        var paths = entries.ToDictionary(
            entry => entry.ArtifactID,
            entry => entry.SuggestedPath ?? string.Empty,
            StringComparer.OrdinalIgnoreCase
        );

        while (true)
        {
            RenderHeader();

            Table table = new Table().Border(TableBorder.Rounded);
            table.AddColumn(new TableColumn("Artifact").LeftAligned());
            table.AddColumn(new TableColumn("Status").LeftAligned());
            table.AddColumn(new TableColumn("Archive path").LeftAligned());

            foreach (var entry in entries)
            {
                string currentPath = paths[entry.ArtifactID];

                string required = entry.Required && string.IsNullOrWhiteSpace(currentPath)
                    ? "[red]Required[/]"
                    : entry.Required
                        ? "[yellow]Required (set)[/]"
                        : "[gray]Optional[/]";

                string shownPath = string.IsNullOrWhiteSpace(currentPath)
                    ? "[gray](not set)[/]"
                    : $"[gray]{Escape(currentPath)}[/]";

                table.AddRow(Escape(entry.DisplayName), required, shownPath);
            }

            AnsiConsole.MarkupLine($"[{ToMarkupColor(AppTheme.OrangeStyle.Foreground)}]{Escape(title)}[/]");

            if (!string.IsNullOrWhiteSpace(details))
                AnsiConsole.MarkupLine($"[gray]{Escape(details)}[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            string action = PromptSelection(
                "Select an action",
                [..entries
                    .Select(entry => new MenuOption<string>(entry.ArtifactID, $"Edit {entry.DisplayName}", "Set or clear a local archive path"))
                    .Append(new MenuOption<string>("__continue", "Continue", "Start install with these values"))]
            );

            if (string.Equals(action, "__continue", StringComparison.Ordinal))
            {
                string[] missing = [..entries
                    .Where(entry => entry.Required && string.IsNullOrWhiteSpace(paths[entry.ArtifactID]))
                    .Select(entry => entry.DisplayName)];

                if (missing.Length > 0)
                {
                    WriteError($"Missing required archive path(s): {string.Join(", ", missing)}");
                    Pause();
                    continue;
                }

                return paths;
            }

            ArchivePathEntry selectedEntry = entries.First(entry => string.Equals(entry.ArtifactID, action, StringComparison.OrdinalIgnoreCase));

            string? current = paths[selectedEntry.ArtifactID];
            string updatedPath = PromptText($"Path for {selectedEntry.DisplayName} archive (leave blank to clear)", string.IsNullOrWhiteSpace(current) ? null : current);

            paths[selectedEntry.ArtifactID] = updatedPath;
        }
    }
}

internal sealed record MenuOption<TValue>(TValue Value, string Label, string Description = "");
internal sealed record ProgressOperation(string Description, Func<ProgressTask, CancellationToken, Task> Action);
internal sealed record ArchivePathEntry(string ArtifactID, string DisplayName, string? SuggestedPath, bool Required);