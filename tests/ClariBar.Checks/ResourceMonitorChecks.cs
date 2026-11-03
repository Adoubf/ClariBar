using ClariBar.Core;

internal static class ResourceMonitorChecks
{
    internal static void Run(Action<bool, string> require, string root)
    {
        var volumes = new[] { new DiskVolumeSpace("volume-a", 100, 50), new DiskVolumeSpace("volume-b", 900, 180) };
        var summary = DiskSpaceMetrics.Summarize(volumes);
        require(summary.VolumeCount == 2 && summary.TotalBytes == 1000 && summary.UsedBytes == 770 && summary.UsedPercent == 77,
            "Total disk space usage is weighted by capacity, not an average of volume percentages");
        var duplicate = DiskSpaceMetrics.Summarize(volumes.Concat([
            new("VOLUME-A", 100, 90), new("empty", 0, 0), new("invalid", 100, 101), new("negative", 100, -1), new("", 100, 0)]));
        require(duplicate == summary, "Duplicate volume aliases and invalid capacity readings do not inflate disk totals");
        var large = DiskSpaceMetrics.Summarize([new("a", long.MaxValue, 0), new("b", long.MaxValue, long.MaxValue)]);
        require(large.VolumeCount == 2 && large.UsedPercent == 50, "Disk aggregation cannot overflow a signed 64-bit combined capacity");
        require(DiskSpaceMetrics.Reading([new("a", 100, 100)]).Value == 0 && DiskSpaceMetrics.Reading([new("a", 100, 0)]).Value == 100,
            "Empty and full disks produce valid zero and one hundred percent readings");
        var unavailable = DiskSpaceMetrics.Reading([]);
        require(!unavailable.IsValid && unavailable.Value is null && unavailable.DisplayValue == "—",
            "No accessible fixed volumes is unavailable rather than a false zero percent");
        var total = DiskSpaceMetrics.Reading(volumes);
        var snapshot = new SensorSnapshot(1, DateTimeOffset.UtcNow, false, "ready", [total,
            new("disk-busy", "/physical/disk/0", "SSD", "Storage", "Total Activity", "Load", 20, "%", "valid"),
            new("disk-read", "/physical/disk/0", "SSD", "Storage", "Read Rate", "Throughput", 1024, "B/s", "valid")], []);
        require(SensorCatalog.Common(snapshot).Count(sensor => sensor.Id == DiskSpaceMetrics.UsageId) == 1 &&
            SensorCatalog.Common(snapshot).Any(sensor => sensor.Id == "disk-busy") && SensorCatalog.Defaults(snapshot).Contains(total),
            "Aggregate disk capacity appears once in common metrics without replacing per-disk busy time or transfer readings");
        string language = Localizer.LanguageCode;
        try
        {
            Localizer.Configure("zh-CN");
            require(total.DisplayName == "磁盘总占用" && total.DisplayHardware == "所有本地固定磁盘",
                "Disk capacity aggregate has clear Chinese labels and a separate aggregate device");
            require(SensorCatalog.Filter(snapshot, Localizer.T("Usage only"), [], false).Contains(total),
                "The disk capacity aggregate is available under the usage filter without administrator access");
            Localizer.Configure("en-US");
            require(total.DisplayName == "Total disk space usage" && total.DisplayHardware == "All local fixed disks",
                "Disk aggregate labels switch languages without changing their stable identity");
        }
        finally { Localizer.Configure(language); }

        var settings = new AppSettings
        {
            PinnedSensors = ["cpu", "upload", "missing-device", DiskSpaceMetrics.UsageId],
            SensorColors = new() { ["cpu"] = "#123456", [DiskSpaceMetrics.UsageId] = "#ABCDEF" },
            SensorNames = new() { ["cpu"] = "Custom CPU", ["missing-device"] = "Disconnected fan" }
        };
        string[] original = settings.PinnedSensors.ToArray();
        string[] reordered = [DiskSpaceMetrics.UsageId, "cpu", "missing-device", "upload"];
        require(PinnedMetricOrder.TryReorder(settings, reordered, []) && settings.PinnedSensors.SequenceEqual(reordered),
            "Reordering moves complete sensor identities and retains disconnected pinned devices");
        var store = new SettingsStore(Path.Combine(root, "metric-order")); store.Save(settings);
        var restored = store.Load();
        require(restored.PinnedSensors.SequenceEqual(reordered) && restored.SensorColors["cpu"] == "#123456" &&
            restored.SensorColors[DiskSpaceMetrics.UsageId] == "#ABCDEF" && restored.SensorNames["missing-device"] == "Disconnected fan",
            "The display order survives restart with aliases and per-metric colors attached to the same sensors");
        foreach (string[] invalid in new[] { reordered[..3], new[] { "cpu", "cpu", "missing-device", "upload" }, new[] { "cpu", "unknown", "missing-device", "upload" } })
            require(!PinnedMetricOrder.TryReorder(settings, invalid, []) && settings.PinnedSensors.SequenceEqual(reordered),
                "Intermediate removal, duplicate IDs and unrelated drag data cannot delete or replace pinned metrics");
        require(!PinnedMetricOrder.TryReorder(settings, reordered, []) && settings.PinnedSensors.SequenceEqual(reordered),
            "A cancelled or same-position reorder leaves the existing order unchanged");
        var automatic = new AppSettings();
        require(!PinnedMetricOrder.TryReorder(automatic, original, original) && automatic.PinnedSensors.Length == 0,
            "Opening the order editor does not turn automatic default selection into explicit pins");
        require(PinnedMetricOrder.TryReorder(automatic, reordered, original) && automatic.PinnedSensors.SequenceEqual(reordered),
            "Reordering automatic defaults materializes the displayed selection only after a real edit");
        require(PinnedMetricOrder.Columns("TwoColumns", 7) == 2 && PinnedMetricOrder.Columns("TwoRows", 7) == 4 &&
            PinnedMetricOrder.Columns("SingleLine", 7) == 7 && PinnedMetricOrder.Columns("TwoRows", 0) == 1,
            "Position hints agree with the taskbar's left-to-right row layout for odd and empty metric counts");
    }
}
