using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace BadBuilder.Services.Disks;

internal static partial class DiskService
{
    [SupportedOSPlatform("windows")]
    private static List<DiskInfo> EnumerateDisksWindows()
    {
        List<DiskInfo> disks = [];

        using ManagementObjectSearcher searcher = new("SELECT * FROM Win32_DiskDrive");

        foreach (ManagementObject drive in searcher.Get())
        {
            int index            = Convert.ToInt32(drive["Index"]);
            string deviceId      = (string)drive["DeviceID"];
            string model         = (drive["Model"] as string)?.Trim() ?? $"Disk {index}";
            long size            = drive["Size"] is null ? 0 : Convert.ToInt64(drive["Size"]);
            string interfaceType = drive["InterfaceType"] as string ?? "";
            string mediaType     = drive["MediaType"] as string ?? "";

            bool removable =
                interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("Removable", StringComparison.OrdinalIgnoreCase);

            disks.Add(
                new DiskInfo(
                    ID: index.ToString(),
                    Name: model,
                    Size: size,
                    Type: removable ? DriveType.Removable : DriveType.Fixed,
                    DevicePath: deviceId
                )
            );
        }

        return disks;
    }

    [SupportedOSPlatform("windows")]
    private static RawDiskStream OpenRawDiskForWriteWindows(DiskInfo disk)
    {
        int diskIndex                = int.Parse(disk.ID);
        List<VolumeLock> volumeLocks = LockAndDismountVolumes(diskIndex);
        string path                  = $@"\\.\PhysicalDrive{diskIndex}";

        SafeFileHandle handle = CreateFile(
            path,
            GENERIC_READ    | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero
        );

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            foreach (VolumeLock l in volumeLocks) l.Dispose();
            throw new IOException($"Could not open {path} for writing (run elevated, as Administrator). Win32 error {error}.");
        }

        FileStream fileStream = new(handle, FileAccess.ReadWrite);

        return new RawDiskStream(fileStream, disk.Size, onDisposed: () =>
        {
            foreach (VolumeLock l in volumeLocks) l.Dispose();
        });
    }

    [SupportedOSPlatform("windows")]
    private static string ReassignWindows(DiskInfo disk)
    {
        int diskIndex = int.Parse(disk.ID);

        using (SafeFileHandle handle = CreateFile(
            $@"\\.\PhysicalDrive{diskIndex}",
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero
        ))
        {
            if (!handle.IsInvalid)
                DeviceIoControl(handle, IOCTL_DISK_UPDATE_PROPERTIES, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }

        string driveLetter = EnumerateDriveLetters(diskIndex).FirstOrDefault()
            ?? throw new IOException($"Formatted PhysicalDrive{diskIndex} but no drive letter was assigned.");

        return driveLetter + @"\";
    }


    [SupportedOSPlatform("windows")]
    private static List<VolumeLock> LockAndDismountVolumes(int diskIndex)
    {
        List<VolumeLock> locks = [];

        foreach (string driveLetter in EnumerateDriveLetters(diskIndex))
        {
            VolumeLock? volumeLock = VolumeLock.TryCreate(driveLetter);
            if (volumeLock is not null)
                locks.Add(volumeLock);
        }

        return locks;
    }


    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> EnumerateDriveLetters(int diskIndex)
    {
        using ManagementObjectSearcher partitionSearcher = new(
            $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='\\\\.\\PHYSICALDRIVE{diskIndex}'}} " +
            "WHERE AssocClass = Win32_DiskDriveToDiskPartition"
        );

        foreach (ManagementObject partition in partitionSearcher.Get())
        {
            using ManagementObjectSearcher logicalSearcher = new(
                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} " +
                "WHERE AssocClass = Win32_LogicalDiskToPartition"
            );

            foreach (ManagementObject logicalDisk in logicalSearcher.Get())
                yield return (string)logicalDisk["DeviceID"];
        }
    }


    private const uint GENERIC_READ                 = 0x80000000;
    private const uint GENERIC_WRITE                = 0x40000000;
    private const uint FILE_SHARE_READ              = 0x1;
    private const uint FILE_SHARE_WRITE             = 0x2;
    private const uint OPEN_EXISTING                = 3;
    private const uint FSCTL_LOCK_VOLUME            = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME        = 0x00090020;
    private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);


    private sealed class VolumeLock : IDisposable
    {
        private readonly SafeFileHandle _handle;

        private VolumeLock(SafeFileHandle handle) => _handle = handle;

        public static VolumeLock? TryCreate(string driveLetter)
        {
            string path = $@"\\.\{driveLetter.TrimEnd('\\')}";

            SafeFileHandle handle = CreateFile(
                path, 
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, 
                OPEN_EXISTING, 
                0, 
                IntPtr.Zero
            );

            if (handle.IsInvalid) return null;

            DeviceIoControl(handle, FSCTL_LOCK_VOLUME,     IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            DeviceIoControl(handle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

            return new VolumeLock(handle);
        }

        public void Dispose() => _handle.Dispose();
    }
}