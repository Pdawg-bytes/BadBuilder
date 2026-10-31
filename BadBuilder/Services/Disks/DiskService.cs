using DiscUtils.Raw;
using DiscUtils.Fat;
using DiscUtils.Streams;
using System.Diagnostics;
using DiscUtils.Partitions;

namespace BadBuilder.Services.Disks;

internal static partial class DiskService
{
    internal static List<DiskInfo> EnumerateDisks()
    {
#if OS_WINDOWS
        return EnumerateDisksWindows();
#elif OS_LINUX
        return EnumerateDisksLinux();
#else
        throw new PlatformNotSupportedException($"DiskService does not support this platform ({Environment.OSVersion.Platform}).");
#endif
    }

    internal static string FormatFAT32(DiskInfo disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        using RawDiskStream stream = OpenRawDiskForWrite(disk);
        using Disk virtualDisk     = new(stream, Ownership.None);

        BiosPartitionTable.Initialize(virtualDisk, WellKnownPartitionType.WindowsFat);

        using FatFileSystem fs = FatFileSystem.FormatPartition(virtualDisk, 0, "BADUPDATE  ");
        stream.Flush();

#if OS_WINDOWS
        return ReassignWindows(disk);
#elif OS_LINUX
        return ReassignLinux(disk);
#else
        throw new PlatformNotSupportedException($"DiskService does not support this platform ({Environment.OSVersion.Platform}).");
#endif
    }

    private static RawDiskStream OpenRawDiskForWrite(DiskInfo disk)
    {
#if OS_WINDOWS
        return OpenRawDiskForWriteWindows(disk);
#elif OS_LINUX
        return OpenRawDiskForWriteLinux(disk);
#else
        throw new PlatformNotSupportedException($"DiskService does not support this platform ({Environment.OSVersion.Platform}).");
#endif
    }


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
