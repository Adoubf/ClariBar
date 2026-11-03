using System.Runtime.InteropServices;
using System.Text;
using ClariBar.Core;

namespace ClariBar.SensorBroker;

internal sealed class DiskSpaceCollector
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindFirstVolume(StringBuilder volumeName, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolume(nint handle, StringBuilder volumeName, int length);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindVolumeClose(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNamesForVolumeName(string volume, [Out] char[] paths, uint capacity, out uint required);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

    private SensorReading reading = DiskSpaceMetrics.Reading([]);
    private long nextSample;

    public SensorReading Read()
    {
        long now = Environment.TickCount64;
        if (now < nextSample) return reading;
        nextSample = now + 5000;
        var volumes = new List<DiskVolumeSpace>();
        var name = new StringBuilder(1024);
        nint handle = FindFirstVolume(name, name.Capacity);
        if (handle == -1) return reading = DiskSpaceMetrics.Reading([]);
        try
        {
            do
            {
                string id = name.ToString();
                if (GetDriveType(id) != 3) continue; // DRIVE_FIXED; excludes network, optical and removable volumes.
                var paths = new char[1024];
                bool mounted = GetVolumePathNamesForVolumeName(id, paths, (uint)paths.Length, out uint required);
                if (!mounted && required is > 1024 and <= 32768)
                {
                    paths = new char[required];
                    mounted = GetVolumePathNamesForVolumeName(id, paths, (uint)paths.Length, out _);
                }
                // Include folder-mounted data volumes as well as drive letters;
                // leave unmounted recovery/system volumes out of user capacity.
                if (!mounted || paths[0] == '\0') continue;
                if (GetDiskFreeSpaceEx(id, out _, out ulong total, out ulong free) && total <= long.MaxValue && free <= long.MaxValue)
                    volumes.Add(new(id, (long)total, (long)free));
            } while (FindNextVolume(handle, name, name.Capacity));
        }
        finally { FindVolumeClose(handle); }
        return reading = DiskSpaceMetrics.Reading(volumes);
    }
}
