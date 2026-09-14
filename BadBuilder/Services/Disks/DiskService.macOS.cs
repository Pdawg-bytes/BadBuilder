using System.Runtime.Versioning;
using System.Xml.Linq;

namespace BadBuilder.Services.Disks;

internal static partial class DiskService
{
    [SupportedOSPlatform("macos")]
    private static List<DiskInfo> EnumerateDisksMacos()
    {
        XElement root = ParsePlist(RunProcess("diskutil", "list -plist"));

        List<DiskInfo> disks = [];

        foreach (string deviceId in GetArrayStrings(root, "WholeDisks"))
        {
            XElement info = ParsePlist(RunProcess("diskutil", $"info -plist /dev/{deviceId}"));

            if (GetBool(info, "Internal"))   continue;
            if (GetBool(info, "SystemImage")) continue;

            if (string.Equals(GetString(info, "VirtualOrPhysical"), "Virtual", StringComparison.OrdinalIgnoreCase))
                continue;

            long size = GetLong(info, "Size") ?? 0;
            if (size <= 0) continue;

            string mediaName = GetString(info, "MediaName") ?? "";
            if (string.IsNullOrWhiteSpace(mediaName)) mediaName = $"Disk {deviceId}";

            string protocol = GetString(info, "BusProtocol") ?? "";
            bool removable  = GetBool(info, "Removable") ||
                              protocol.Equals("USB", StringComparison.OrdinalIgnoreCase);

            disks.Add(new DiskInfo(
                ID: deviceId,
                Name: mediaName,
                Size: size,
                Type: removable ? DriveType.Removable : DriveType.Fixed,
                DevicePath: $"/dev/{deviceId}"
            ));
        }

        return disks;
    }

    [SupportedOSPlatform("macos")]
    private static RawDiskStream OpenRawDiskForWriteMacos(DiskInfo disk)
    {
        string devicePath = $"/dev/{disk.ID}";

        RunProcess("diskutil", $"unmountDisk force {devicePath}");

        FileStream fileStream = new(
            $"/dev/r{disk.ID}",
            new FileStreamOptions
            {
                Mode       = FileMode.Open,
                Access     = FileAccess.ReadWrite,
                BufferSize = 512,
            }
        );

        return new RawDiskStream(fileStream, disk.Size);
    }

    [SupportedOSPlatform("macos")]
    private static string ReassignMacos(DiskInfo disk)
    {
        string devicePath = $"/dev/{disk.ID}";

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Thread.Sleep(500);

            string listOutput = RunProcess("diskutil", $"list -plist {devicePath}");

            XElement list  = ParsePlist(listOutput);
            string? mount  = GetArrayDicts(list, "AllDisksAndPartitions")
                .SelectMany(entry => GetArrayDicts(entry, "Partitions"))
                .Select(partition => GetString(partition, "MountPoint"))
                .FirstOrDefault(mountPoint => !string.IsNullOrWhiteSpace(mountPoint));

            if (mount is not null) return mount;

            if (attempt % 2 == 0)
                RunProcess("diskutil", $"mountDisk {devicePath}");
        }

        throw new IOException($"Formatted {devicePath} but no volume was mounted. Mount the drive manually and copy the files.");
    }


    private static XElement ParsePlist(string plist)
    {
        int doctypeStart = plist.IndexOf("<!DOCTYPE", StringComparison.Ordinal);
        if (doctypeStart >= 0)
        {
            int doctypeEnd = plist.IndexOf('>', doctypeStart);
            if (doctypeEnd >= 0)
                plist = plist.Remove(doctypeStart, doctypeEnd - doctypeStart + 1);
        }

        return XDocument.Parse(plist).Root?.Element("dict")
            ?? throw new InvalidOperationException("Unexpected plist format from diskutil.");
    }

    private static XElement? SiblingAfterKey(XElement dict, string key)
    {
        foreach (XElement keyElement in dict.Elements("key"))
        {
            if (keyElement.Value != key) continue;
            return keyElement.ElementsAfterSelf().FirstOrDefault();
        }

        return null;
    }

    private static string? GetString(XElement dict, string key)
    {
        XElement? sibling = SiblingAfterKey(dict, key);
        return sibling?.Name == "string" ? sibling.Value : null;
    }

    private static bool GetBool(XElement dict, string key)
    {
        XElement? sibling = SiblingAfterKey(dict, key);
        return sibling?.Name == "true";
    }

    private static long? GetLong(XElement dict, string key)
    {
        XElement? sibling = SiblingAfterKey(dict, key);
        return sibling?.Name == "integer" && long.TryParse(sibling.Value, out long value) ? value : null;
    }

    private static IEnumerable<string> GetArrayStrings(XElement dict, string key)
    {
        XElement? sibling = SiblingAfterKey(dict, key);
        if (sibling?.Name != "array") yield break;

        foreach (XElement child in sibling.Elements())
            if (child.Name == "string")
                yield return child.Value;
    }

    private static IEnumerable<XElement> GetArrayDicts(XElement dict, string key)
    {
        XElement? sibling = SiblingAfterKey(dict, key);
        if (sibling?.Name != "array") yield break;

        foreach (XElement child in sibling.Elements())
            if (child.Name == "dict")
                yield return child;
    }
}