﻿using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BadBuilder
{
    internal partial class Program
    {
        static bool PromptAddHomebrew()
        {
            return AnsiConsole.Prompt(
                new TextPrompt<bool>("Would you like to add homebrew programs?")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(false)
                .ChoicesStyle(GreenStyle)
                .DefaultValueStyle(OrangeStyle)
                .WithConverter(choice => choice ? "y" : "n")
            );
        }

        public static List<HomebrewApp> ManageHomebrewApps()
        {
            var homebrewApps = new List<HomebrewApp>();

            while (true)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .PageSize(4)
                        .HighlightStyle(GreenStyle)
                        .AddChoices("Add Homebrew App", "View Added Apps", "Remove App", "Finish & Save")
                );

                switch (choice)
                {
                    case "Add Homebrew App":
                        ClearConsole();

                        var newApp = AddHomebrewApp();
                        if (newApp != null)
                        {
                            homebrewApps.Add(newApp.Value);
                            AnsiConsole.Status()
                                .Start("Adding...", ctx => ctx.Spinner(Spinner.Known.Dots2));
                        }
                        break;

                    case "View Added Apps":
                        ClearConsole();
                        DisplayApps(homebrewApps);
                        break;

                    case "Remove App":
                        ClearConsole();
                        RemoveHomebrewApp(homebrewApps);
                        break;

                    case "Finish & Save":
                        if (homebrewApps.Count == 0)
                        {
                            AnsiConsole.MarkupLine("[#ffac4d]No apps added.[/]");
                            return homebrewApps;
                        }

                        if (AnsiConsole.Prompt(
                                new TextPrompt<bool>("Save and exit?")
                                .AddChoice(true)
                                .AddChoice(false)
                                .DefaultValue(true)
                                .ChoicesStyle(GreenStyle)
                                .DefaultValueStyle(OrangeStyle)
                                .WithConverter(choice => choice ? "y" : "n")))
                        {
                            string status = "[+]";
                            AnsiConsole.MarkupInterpolated($"[#76B900]{status}[/] Saved [bold]{homebrewApps.Count}[/] app(s).\n");
                            return homebrewApps;
                        }
                        break;
                }
            }
        }

        private static HomebrewApp? AddHomebrewApp() 
        { 
            AnsiConsole.MarkupLine("[bold #76B900]Add a new homebrew app[/]\n"); 
            string folderPath = AnsiConsole.Ask<string>("[#FFA500]Enter the folder path for the app:[/]"); 
            if (!Directory.Exists(folderPath)) 
            { 
                AnsiConsole.MarkupLine("[#ffac4d]Invalid folder path. Please try again.[/]"); 
                return null; 
            } 
            
            string[] xexFiles = Directory.GetFiles(folderPath, "*.xex"); 
            if (xexFiles.Length == 0) 
            { 
                AnsiConsole.MarkupLine("[#ffac4d]No XEX files found in this folder.[/]"); 
                return null; 
            }

            string entryPoint = xexFiles.Length switch
            {
                0 => AnsiConsole.Prompt(
                    new TextPrompt<string>("[#ffac4d]No .xex files found.[/] Enter entry point:")
                        .Validate(path => File.Exists(path) ? ValidationResult.Success() : ValidationResult.Error("File not found"))
                ),
                1 => xexFiles[0],
                _ => AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[#FFA500]Select entry point:[/]")
                        .HighlightStyle(PeachStyle)
                        .AddChoices(xexFiles.Select(Path.GetFileName))
                )
            };

            ClearConsole();

            AnsiConsole.MarkupLine($"[#76B900]Added:[/] {folderPath.Split('\\').Last()} -> [#ffac4d]{Path.GetFileName(entryPoint)}[/]\n");
            return (folderPath.Split('\\').Last(), folderPath, Path.Combine(folderPath, entryPoint)); 
        }



        private static void DisplayApps(List<HomebrewApp> apps)
        {
            if (apps.Count == 0)
            {
                AnsiConsole.MarkupLine("[#ffac4d]No homebrew apps added.[/]\n");
                return;
            }

            var table = new Table()
                .Title("[bold #76B900]Added Homebrew Apps[/]")
                .AddColumn("[#4D8C00]Folder[/]")
                .AddColumn("[#ff7200]Entry Point[/]");

            foreach (var app in apps)
                table.AddRow($"[#A1CF3E]{app.folder}[/]", $"[#ffac4d]{Path.GetFileName(app.entryPoint)}[/]");
            
            AnsiConsole.Write(table);
            Console.WriteLine();
        }

        private static void RemoveHomebrewApp(List<HomebrewApp> apps)
        {
            if (apps.Count == 0)
            {
                AnsiConsole.MarkupLine("[#ffac4d]No apps to remove.[/]\n");
                return;
            }

            var appToRemove = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold #76B900]Select an app to remove:[/]")
                    .PageSize(5)
                    .HighlightStyle(LightOrangeStyle)
                    .MoreChoicesText("[grey](Move up/down to scroll)[/]")
                    .AddChoices(apps.Select(app => $"{Path.GetFileName(app.folder)}"))
            );

            var selectedApp = apps.First(app => $"{Path.GetFileName(app.folder)}" == appToRemove);
            apps.Remove(selectedApp);

            AnsiConsole.MarkupLine($"[#ffac4d]Removed:[/] {selectedApp.folder.Split('\\').Last()}\n");
        }
    }
}
