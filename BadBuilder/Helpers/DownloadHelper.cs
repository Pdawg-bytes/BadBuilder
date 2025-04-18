﻿using Octokit;
using Spectre.Console;

using static BadBuilder.Utilities.Constants;

namespace BadBuilder.Helpers
{
    internal static class DownloadHelper
    {
        internal static async Task GetGitHubAssets(List<DownloadItem> items)
        {
            GitHubClient gitClient = new(new ProductHeaderValue("BadBuilder-Downloader"));
            List<string> repos =
            [
                "grimdoomer/Xbox360BadUpdate",
                "Byrom90/XeUnshackle",
                "FreeMyXe/FreeMyXe"
            ];

            foreach (var repo in repos)
            {
                string[] splitRepo = repo.Split('/');
                var latestRelease = await gitClient.Repository.Release.GetLatest(splitRepo[0], splitRepo[1]);

                foreach (var asset in latestRelease.Assets)
                {
                    string friendlyName = asset.Name switch
                    {
                        var name when name.Contains("Free", StringComparison.OrdinalIgnoreCase) => "FreeMyXe",
                        var name when name.Contains("Tools", StringComparison.OrdinalIgnoreCase) => "BadUpdate Tools",
                        var name when name.Contains("BadUpdate", StringComparison.OrdinalIgnoreCase) => "BadUpdate",
                        var name when name.Contains("XeUnshackle", StringComparison.OrdinalIgnoreCase) => "XeUnshackle",
                        _ => asset.Name.Substring(0, asset.Name.Length - 4)
                    };

                    items.Add(new(friendlyName, asset.BrowserDownloadUrl));
                }
            }
        }

        internal static async Task DownloadFileAsync(HttpClient client, ProgressTask task, string url)
        {
            try
            {
                byte[] downloadBuffer = new byte[8192];
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    task.MaxValue(response.Content.Headers.ContentLength ?? 0);
                    task.StartTask();

                    string filename = url.Substring(url.LastIndexOf('/') + 1);

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new($"{DOWNLOAD_DIR}/{filename}", System.IO.FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        Array.Clear(downloadBuffer);
                        while (true)
                        {
                            var read = await contentStream.ReadAsync(downloadBuffer, 0, downloadBuffer.Length);
                            if (read == 0)
                                break;

                            task.Increment(read);
                            await fileStream.WriteAsync(downloadBuffer, 0, read);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error downloading file from [bold]{url}[/]: {ex}[/]");
            }
        }
    }
}