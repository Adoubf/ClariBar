using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

public sealed partial class MainWindow
{
    private PinnedMetricOrderEditor? metricOrderEditor;
    private ContentDialog? metricOrderDialog;

    private async Task ShowMetricOrderAsync()
    {
        if (metricOrderDialog is not null || quitting) return;
        var editor = new PinnedMetricOrderEditor(settings, MonitorSensors, () =>
        {
            if (quitting) return;
            Save(); UpdateMonitor();
            if (page == "sensors") RefreshSensorRows();
        });
        void ResizeEditor()
        {
            editor.MinWidth = Math.Min(620, Math.Max(240, Root.ActualWidth - 160));
            editor.MaxHeight = Math.Max(180, Root.ActualHeight - 220);
        }
        SizeChangedEventHandler resized = (_, _) => ResizeEditor();
        ResizeEditor(); Root.SizeChanged += resized;
        var dialog = new ContentDialog
        {
            Title = L.T("Pinned metric order"), Content = editor, CloseButtonText = L.T("Done"), XamlRoot = Root.XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 780d;
        metricOrderEditor = editor; metricOrderDialog = dialog;
        UpdateMonitor();
        try { await dialog.ShowAsync(); }
        finally { Root.SizeChanged -= resized; metricOrderEditor = null; metricOrderDialog = null; }
    }
}
