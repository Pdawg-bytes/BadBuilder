using DiscUtils.Raw;
using DiscUtils.Fat;
using DiscUtils.Streams;
using System.Diagnostics;
using DiscUtils.Partitions;

namespace BadBuilder.Services.Disks;

internal static partial class DiskService
{
    internal static List<DiskInfo> EnumerateDisks() => InvokePlatformAction(EnumerateDisksWindows, EnumerateDisksMacos, null);

    internal static string FormatFAT32(DiskInfo disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        {
            using RawDiskStream stream = OpenRawDiskForWrite(disk);
            using Disk virtualDisk     = new(stream, Ownership.None);

            BiosPartitionTable.Initialize(virtualDisk, WellKnownPartitionType.WindowsFat);

            using FatFileSystem fs = FatFileSystem.FormatPartition(virtualDisk, 0, "BADUPDATE  ");
            stream.Flush();
        }

        return InvokePlatformAction(ReassignWindows, ReassignMacos, null, disk);
    }

    private static RawDiskStream OpenRawDiskForWrite(DiskInfo disk)
        => InvokePlatformAction(OpenRawDiskForWriteWindows, OpenRawDiskForWriteMacos, null, disk);


    private static string RunProcess(string fileName, string arguments)
    {
        ProcessStartInfo psi = new(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };

        using Process process = Process.Start(psi) ?? throw new IOException($"Failed to start '{fileName}'.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new IOException($"'{fileName} {arguments}' failed ({process.ExitCode}): {stderr}");

        return stdout;
    }


    private static T InvokePlatformAction<T>(Func<T> windowsAction, Func<T> macosAction, Func<T> linuxAction)
    {
        if (OperatingSystem.IsWindows()) return windowsAction();
        if (OperatingSystem.IsMacOS()) return macosAction();
        if (OperatingSystem.IsLinux()) return linuxAction();

        throw new PlatformNotSupportedException($"DiskService does not support this platform ({Environment.OSVersion.Platform}).");
    }

    private static T InvokePlatformAction<TIn, T>(Func<TIn, T> windowsAction, Func<TIn, T> macosAction, Func<TIn, T> linuxAction, TIn input)
    {
        if (OperatingSystem.IsWindows()) return windowsAction(input);
        if (OperatingSystem.IsMacOS()) return macosAction(input);
        if (OperatingSystem.IsLinux()) return linuxAction(input);

        throw new PlatformNotSupportedException($"DiskService does not support this platform ({Environment.OSVersion.Platform}).");
    }


    private sealed class RawDiskStream(Stream inner, long length, Action? onDisposed = null) : Stream
    {
        private readonly Stream _inner       = inner;
        private readonly long _length        = length;
        private readonly Action? _onDisposed = onDisposed;
        private bool _disposed;

        public override bool CanRead  => _inner.CanRead;
        public override bool CanSeek  => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length   => _length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush()                                     => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)   => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin)        => _inner.Seek(offset, origin);
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _inner.Dispose();
                _onDisposed?.Invoke();
                _disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}

internal sealed record DiskInfo(
    string ID,
    string Name,
    long Size,
    DriveType Type,
    string DevicePath);