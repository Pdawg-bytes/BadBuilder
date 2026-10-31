using System.Runtime.Versioning;
using System.Text.Json;

namespace BadBuilder.Services.Disks;

internal static partial class DiskService
{
    [SupportedOSPlatform("linux")]
    private static List<DiskInfo> EnumerateDisksLinux()
    {
        string json;
        try
        {
            json = RunProcess("lsblk", "-J -d -b -o NAME,SIZE,TYPE,TRAN,MODEL,RM");
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Failed to enumerate disks using lsblk. Ensure util-linux is installed: {ex.Message}", ex);
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("blockdevices", out JsonElement devices))
            return [];

        List<DiskInfo> disks = [];

        foreach (JsonElement device in devices.EnumerateArray())
        {
            string name = device.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string type = device.TryGetProperty("type", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
            if (!type.Equals("disk", StringComparison.OrdinalIgnoreCase))
                continue;

            long size = 0;
            if (device.TryGetProperty("size", out var s))
            {
                if (s.ValueKind == JsonValueKind.Number)
                    s.TryGetInt64(out size);
                else if (s.ValueKind == JsonValueKind.String)
                    long.TryParse(s.GetString(), out size);
            }

            string tran  = device.TryGetProperty("tran",  out var tr) ? (tr.GetString()?.Trim() ?? string.Empty) : string.Empty;
            string model = device.TryGetProperty("model", out var m)  ? (m.GetString()?.Trim()  ?? string.Empty) : string.Empty;

            bool removable = false;
            if (device.TryGetProperty("rm", out var rm))
            {
                removable = rm.ValueKind switch
                {
                    JsonValueKind.True   => true,
                    JsonValueKind.False  => false,
                    JsonValueKind.Number => rm.GetInt32() != 0,
                    JsonValueKind.String => rm.GetString() == "1",
                    _                    => false,
                };
            }

            if (!removable && tran.Equals("usb", StringComparison.OrdinalIgnoreCase))
                removable = true;

            string devicePath  = $"/dev/{name}";
            string displayName = string.IsNullOrWhiteSpace(model) ? name : model;

            disks.Add(new DiskInfo(
                ID:         name,
                Name:       displayName,
                Size:       size,
                Type:       removable ? DriveType.Removable : DriveType.Fixed,
                DevicePath: devicePath
            ));
        }

        return disks;
    }

    [SupportedOSPlatform("linux")]
    private static RawDiskStream OpenRawDiskForWriteLinux(DiskInfo disk)
    {
        UnmountPartitionsLinux(disk.DevicePath);

        FileStream stream = new(
            disk.DevicePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None
        );

        return new RawDiskStream(stream, disk.Size);
    }

    [SupportedOSPlatform("linux")]
    private static void UnmountPartitionsLinux(string devicePath)
    {
        if (!File.Exists("/proc/mounts"))
            return;

        string[] lines   = File.ReadAllLines("/proc/mounts");
        string devName   = Path.GetFileName(devicePath);

        var partitions = lines
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => parts[0])
            .Where(dev =>
            {
                string partName = Path.GetFileName(dev);
                return partName.StartsWith(devName, StringComparison.Ordinal)
                    && partName.Length > devName.Length;
            })
            .Distinct()
            .ToList();

        foreach (string partition in partitions)
        {
            try { RunProcess("umount", partition); }
            catch { }
        }
    }

    [SupportedOSPlatform("linux")]
    private static string ReassignLinux(DiskInfo disk)
    {
        // Notify kernel of partition table changes
        try { RunProcess("partprobe", disk.DevicePath); }
        catch
        {
            try { RunProcess("blockdev", $"--rereadpt {disk.DevicePath}"); }
            catch { }
        }

        // Wait for udev to process events
        try   { RunProcess("udevadm", "settle"); }
        catch { Thread.Sleep(2000); }

        string partition = FindPartitionDeviceLinux(disk.DevicePath);

        // Clean up any previous mount at the target path
        string mountPoint = $"/tmp/badbuilder_{Path.GetFileName(disk.DevicePath)}";
        try { RunProcess("umount", mountPoint); } catch { }
        Directory.CreateDirectory(mountPoint);

        RunProcess("mount", $"{partition} {mountPoint}");

        return mountPoint;
    }

    [SupportedOSPlatform("linux")]
    private static string FindPartitionDeviceLinux(string devicePath)
    {
        string baseName = Path.GetFileName(devicePath);

        // Standard naming: /dev/sda -> /dev/sda1
        string plain = $"{devicePath}1";
        // NVMe/MMC naming: /dev/nvme0n1 -> /dev/nvme0n1p1, /dev/mmcblk0 -> /dev/mmcblk0p1
        string withP = $"{devicePath}p1";

        for (int i = 0; i < 10; i++)
        {
            if (BlockDeviceExists(baseName + "1"))  return plain;
            if (BlockDeviceExists(baseName + "p1")) return withP;
            Thread.Sleep(500);
        }

        throw new IOException(
            $"Partition device not found after formatting {devicePath}. " +
            $"Try running 'partprobe {devicePath}' or 'partx -u {devicePath}' manually.");
    }

    private static bool BlockDeviceExists(string deviceName) =>
        Directory.Exists($"/sys/class/block/{deviceName}");
}
