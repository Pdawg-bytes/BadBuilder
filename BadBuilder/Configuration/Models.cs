namespace BadBuilder.Configuration;

internal enum ExploitOption
{
    ABadAvatar,
    ABadAvatarUpdate,
    BadUpdate,
}

internal enum BootstrapOption
{
    XeUnshackle,
    FreeMyXe,
}


internal abstract record Source();
internal sealed record   GitHubReleaseSource(string Owner, string Repo, string? ReleaseTag = null, string? AssetName = null) : Source;
internal sealed record   DirectSource(string URL) : Source;

internal sealed record HomebrewEntry(
    ArtifactDefinition Artifact,
    string? SourcePath = null,
    string? EntryPointRelativePath = null);

internal sealed record ArtifactDefinition(
    string ID,
    string DisplayName,
    string Description,
    string DestinationRelativePath,
    Source? Source,
    IReadOnlyList<InstallOperation>? Operations = null,
    ArtifactPriority Priority = ArtifactPriority.Homebrew,
    string? LocalArchivePath = null);

internal enum ArtifactPriority
{
    DashboardUpdate,
    RockBandBlitz,
    Exploit,
    Bootstrap,
    Homebrew,
}


internal sealed record InstallOperation(
    InstallOperationKind Kind,
    string DestinationPath,
    string? SourcePath = null,
    string? Contents = null);

internal enum InstallOperationKind
{
    CopyDirectory,
    CopyFile,
    WriteFile,
    RenameFile,
}