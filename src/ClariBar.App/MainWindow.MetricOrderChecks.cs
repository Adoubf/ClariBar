using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private async Task CheckMetricOrderAsync(Action<bool, string> require)
    {
        var previousBroker = broker; broker = null;
        if (previousBroker is not null) await previousBroker.DisposeAsync();
        string[] pins = settings.PinnedSensors; string layout = settings.MonitorLayout;
        var colors = settings.SensorColors; var names = settings.SensorNames;
        var previousSnapshot = snapshot;
        try
        {
            snapshot = new(1, DateTimeOffset.UtcNow, false, "ready", [
                new("test/cpu", "test/cpu", "CPU", "Cpu", "CPU Total", "Load", 25, "%", "valid"),
                new("test/memory", "/ram", "RAM", "Memory", "Memory", "Load", 40, "%", "valid"),
                new(NetworkMetrics.UploadId, "test/network", "Ethernet", "Network", "Upload Speed", "Throughput", 2048, "B/s", "valid"),
                new(NetworkMetrics.DownloadId, "test/network", "Ethernet", "Network", "Download Speed", "Throughput", 4096, "B/s", "valid"),
                new("test/gpu", "test/gpu", "GPU", "GpuNvidia", "GPU Core", "Load", 10, "%", "valid"),
                DiskSpaceMetrics.Reading([new("a", 100, 50), new("b", 900, 180)])], []);
            settings.PinnedSensors = snapshot.Sensors.Select(sensor => sensor.Id).Append("test/disconnected").ToArray();
            settings.SensorColors = new() { ["test/cpu"] = "#123456", [DiskSpaceMetrics.UsageId] = "#F5A623" };
            settings.SensorNames = new() { ["test/cpu"] = "Custom CPU", ["test/disconnected"] = "Custom fan" };
            settings.MonitorLayout = "TwoColumns";
            category = L.T("All categories"); sensorKind = L.T("Common metrics"); query = deviceId = "";
            NavigateTo("sensors"); await Task.Delay(200);
            require(sensorRows.Any(row => row.Id == DiskSpaceMetrics.UsageId && row.Name == (L.IsChinese ? "磁盘总占用" : "Total disk space usage")),
                "Resource monitor offers the aggregate disk capacity metric in common readings");
            require(sensorRows.Single(row => row.Id == DiskSpaceMetrics.UsageId).Value.Contains("77"),
                "Disk capacity shows the actual weighted percentage without administrator authorization");

            Task dialog = ShowMetricOrderAsync(); await Task.Delay(400);
            var editor = metricOrderEditor ?? throw new IOException("Metric order editor did not open.");
            require(metricOrderDialog!.CloseButtonText == (L.IsChinese ? "完成" : "Done"),
                "Order editor completion button uses the selected display language");
            require(editor.MetricList.CanDragItems && editor.MetricList.CanReorderItems && editor.MetricList.AllowDrop && editor.MetricList.SelectionMode == ListViewSelectionMode.Single,
                "Order editor enables WinUI single-item drag reordering");
            require(editor.Ids.SequenceEqual(settings.PinnedSensors) && editor.Preview.MetricCount == 6,
                "Editor retains an offline pinned sensor while its preview shows the six detected readings");
            string[] before = settings.PinnedSensors.ToArray();
            editor.Move(DiskSpaceMetrics.UsageId, -1);
            var expected = before.ToArray(); (expected[4], expected[5]) = (expected[5], expected[4]);
            require(settings.PinnedSensors.SequenceEqual(expected) && store.Load().PinnedSensors.SequenceEqual(expected),
                "Moving a pinned metric updates persisted display order immediately");
            require(settings.SensorNames["test/cpu"] == "Custom CPU" && settings.SensorColors["test/cpu"] == "#123456" &&
                settings.SensorNames["test/disconnected"] == "Custom fan" && editor.MetricList.Items.Count == 7,
                "Reordering preserves custom labels, colors and temporarily disconnected sensor identities");
            var configured = MonitorConfig();
            require(editor.Preview.MetricTexts.Select(text => text.Text).SequenceEqual(configured.Metrics.Take((int)configured.MetricCount).Select(metric => metric.Text)),
                "Taskbar configuration and live preview render exactly the same metric order");
            foreach (string mode in new[] { "TwoColumns", "TwoRows", "SingleLine" })
            {
                settings.MonitorLayout = mode; UpdateMonitor();
                int columns = PinnedMetricOrder.Columns(mode, 6);
                var disk = editor.MetricList.Items.Cast<PinnedMetricOrderEditor.MetricItem>().Single(item => item.Id == DiskSpaceMetrics.UsageId);
                require(disk.PositionText.Text == L.F("Row {0}, column {1}", 4 / columns + 1, 4 % columns + 1),
                    "Position hints match the live taskbar layout: " + mode);
            }
            settings.MonitorLayout = "TwoColumns"; UpdateMonitor();
            var stableItem = editor.MetricList.Items[0];
            snapshot = snapshot with { CapturedAt = DateTimeOffset.UtcNow, Sensors = snapshot.Sensors.Select(sensor => sensor.Id == "test/cpu" ? sensor with { Value = 99 } : sensor).ToArray() };
            UpdateMonitor();
            require(ReferenceEquals(stableItem, editor.MetricList.Items[0]) &&
                ((PinnedMetricOrderEditor.MetricItem)stableItem).ValueText.Text.Contains("99") && settings.PinnedSensors.SequenceEqual(expected),
                "Live sensor updates refresh values without rebuilding rows or resetting their order");
            editor.Move(editor.Ids[0], -1); editor.Move(editor.Ids[^1], 1);
            require(settings.PinnedSensors.SequenceEqual(expected), "Moving beyond either list edge is a safe no-op");
            foreach (var size in new[] { new SizeInt32(1220, 860), new SizeInt32(800, 640), new SizeInt32(600, 480) })
            {
                AppWindow.Resize(size); await Task.Delay(300);
                require(editor.MinWidth <= Root.ActualWidth - 100 && editor.MaxHeight <= Root.ActualHeight - 160 &&
                    editor.ActualWidth <= Root.ActualWidth && editor.ActualHeight <= Root.ActualHeight,
                    $"Order editor remains within the window at {size.Width}x{size.Height}");
            }
            AppWindow.Resize(new(1220, 860)); await Task.Delay(250);
            await RenderCheckImageAsync(L.LanguageCode + "-metric-order", editor);
            metricOrderDialog!.Hide(); await dialog;
            require(metricOrderEditor is null && metricOrderDialog is null && settings.PinnedSensors.SequenceEqual(expected),
                "Closing the order editor preserves the edited order and releases its live view");
            snapshot = snapshot with { CapturedAt = DateTimeOffset.UtcNow };
            sensorKind = L.T("Pinned"); RefreshSensorRows();
            require(sensorRows.Select(row => row.Id).SequenceEqual(expected.Where(id => snapshot.Sensors.Any(sensor => sensor.Id == id))),
                "The Pinned filter follows the configured taskbar order rather than sorting by hardware name");
        }
        finally
        {
            metricOrderDialog?.Hide(); settings.PinnedSensors = pins; settings.MonitorLayout = layout;
            settings.SensorColors = colors; settings.SensorNames = names; snapshot = previousSnapshot;
        }
    }
}
