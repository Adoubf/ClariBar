namespace ClariBar.Core;

public sealed record DiskVolumeSpace(string VolumeId, long TotalBytes, long FreeBytes);

public sealed record DiskSpaceSummary(decimal TotalBytes, decimal UsedBytes, int VolumeCount)
{
    public float? UsedPercent => TotalBytes > 0 ? (float)(100 * UsedBytes / TotalBytes) : null;
}

public static class DiskSpaceMetrics
{
    public const string HardwareId = "/claribar/storage/total";
    public const string UsageId = HardwareId + "/load/used-space";

    public static DiskSpaceSummary Summarize(IEnumerable<DiskVolumeSpace> volumes)
    {
        // Sum bytes before computing a percentage. Equal weighting of volume
        // percentages would give small system volumes disproportionate influence.
        decimal total = 0, used = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var volume in volumes)
        {
            if (string.IsNullOrWhiteSpace(volume.VolumeId) || volume.TotalBytes <= 0 ||
                volume.FreeBytes < 0 || volume.FreeBytes > volume.TotalBytes || !seen.Add(volume.VolumeId)) continue;
            total += volume.TotalBytes;
            used += volume.TotalBytes - volume.FreeBytes;
        }
        return new(total, used, seen.Count);
    }

    public static SensorReading Reading(IEnumerable<DiskVolumeSpace> volumes)
    {
        var summary = Summarize(volumes);
        return new(UsageId, HardwareId, "All local fixed disks", "Storage", "Total Disk Space Used", "Load",
            summary.UsedPercent, "%", summary.UsedPercent.HasValue ? "valid" : "unavailable");
    }
}
