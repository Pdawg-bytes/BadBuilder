using System.Diagnostics;
using System.Runtime.Versioning;

namespace BadBuilder.Services.Disks;

internal static partial class DiskService
{
    [SupportedOSPlatform("linux")]
    private static List<DiskInfo> EnumerateDisksLinux()
    {
        return new DirectoryInfo("/sys/block")
            .GetDirectories()
            .Where(d => File.Exists($"{d.FullName}/size") && long.Parse(File.ReadAllText($"{d.FullName}/size").Trim()) > 0)
            .Where(d => Directory.Exists($"{d.FullName}/device"))
            .Select(d => {
                string vendorPath = $"{d.FullName}/device/vendor";
                string modelPath = $"{d.FullName}/device/model";
                
                string vendor = File.Exists(vendorPath) ? File.ReadAllText(vendorPath).Trim() : "";
                string model = File.Exists(modelPath) ? File.ReadAllText(modelPath).Trim() : "";
                string hwName = $"{vendor} {model}".Trim();

                return new DiskInfo(
                    ID: d.Name,
                    Name: string.IsNullOrEmpty(hwName) ? d.Name : hwName,
                    Size: File.Exists($"{d.FullName}/size") ? long.Parse(File.ReadAllText($"{d.FullName}/size").Trim()) * 512 : 0,
                    Type: File.Exists($"{d.FullName}/removable") && File.ReadAllText($"{d.FullName}/removable").Trim() == "1" ? DriveType.Removable : DriveType.Fixed,
                    DevicePath: $"/dev/{d.Name}"
                );
            })
            .ToList();
    }
    
    [SupportedOSPlatform("linux")]
    private static void UnmountAll(string rawDevicePath)
    {
        if (!File.Exists("/proc/mounts")) return;
        
        var targets = File.ReadLines("/proc/mounts")
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .Where(dev => dev.StartsWith(rawDevicePath))
            .Distinct()
            .ToList();

        if (targets.Count == 0) return;

        foreach (var target in targets)
        {
            RunProcess("/bin/umount", target);
        }
    }

    [SupportedOSPlatform("linux")]
    private static string FormatFAT32Linux(DiskInfo disk)
    {
        UnmountAll(disk.DevicePath);
        
        RunProcess("/bin/sh", $"-c \"echo -e 'label: dos\\ntype=c' | sfdisk {disk.DevicePath}\"");
        
        if (!File.Exists("/bin/mkfs.fat"))
            throw new Exception(
                "mkfs.fat not found, installation cannot continue. Install dosfstools using your package manager.");
        RunProcess("/bin/mkfs.fat", $"-F 32 -n \"BADUPDATE\" {disk.DevicePath}1");
        
        Directory.CreateDirectory("/tmp/badbuilder-drive");
        RunProcess("/bin/mount", $"{disk.DevicePath}1 /tmp/badbuilder-drive");
        
        return "/tmp/badbuilder-drive/";
    }

    [SupportedOSPlatform("linux")]
    private static void UnmountDiskLinux(DiskInfo disk)
    {
        RunProcess("/bin/umount", $"{disk.DevicePath}1");
    }
}