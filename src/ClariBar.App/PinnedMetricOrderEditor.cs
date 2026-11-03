using System.Collections.ObjectModel;
using ClariBar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using L = ClariBar.Core.Localizer;

namespace ClariBar.App;

internal sealed class PinnedMetricOrderEditor : UserControl
{
    private readonly AppSettings settings;
    private readonly Func<SensorReading[]> monitorSensors;
    private readonly Action changed;
    private readonly ObservableCollection<MetricItem> items = [];
    private readonly TextBlock empty = new() { TextWrapping = TextWrapping.Wrap };
    private bool dragging;
    internal ListView MetricList { get; }
    internal TaskbarPreview Preview { get; } = new();
    internal string[] Ids => items.Select(item => item.Id).ToArray();

    internal PinnedMetricOrderEditor(AppSettings settings, Func<SensorReading[]> monitorSensors, Action changed)
    {
        this.settings = settings; this.monitorSensors = monitorSensors; this.changed = changed;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Drag metrics to change their display order, or use the arrow buttons. Changes are saved immediately and shown in the preview and taskbar."),
            TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        panel.Children.Add(Preview);
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Metrics fill each row from left to right, then continue on the next row. Ctrl+Up / Ctrl+Down moves the selected metric."),
            TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        MetricList = new ListView
        {
            ItemsSource = items, SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = true, CanReorderItems = true, AllowDrop = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsItemClickEnabled = false, MaxHeight = 340
        };
        AutomationProperties.SetName(MetricList, L.T("Pinned metric order"));
        MetricList.DragItemsStarting += (_, args) =>
        {
            if (args.Items.Count != 1) { args.Cancel = true; return; }
            dragging = true; args.Data.RequestedOperation = DataPackageOperation.Move;
        };
        MetricList.DragItemsCompleted += (_, _) =>
        {
            // Let WinUI finish its source collection notifications before
            // validating the final permutation. Internal reorders need not
            // report the same DropResult as an external move operation.
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                dragging = false; Commit();
            })) dragging = false;
        };
        foreach (var (key, direction) in new[] { (VirtualKey.Up, -1), (VirtualKey.Down, 1) })
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) =>
            {
                if (MetricList.SelectedItem is MetricItem item) Move(item.Id, direction);
                args.Handled = true;
            };
            MetricList.KeyboardAccelerators.Add(accelerator);
        }
        panel.Children.Add(MetricList);
        empty.Text = L.T("Select metrics in Resource monitor first. When no metrics are pinned, ClariBar uses the automatic selection.");
        panel.Children.Add(empty);
        Content = new ScrollViewer
        {
            Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    internal void Update(SensorSnapshot snapshot, TaskbarConfig config)
    {
        var visible = monitorSensors();
        string[] desired = settings.PinnedSensors.Length > 0 ? settings.PinnedSensors : visible.Select(sensor => sensor.Id).ToArray();
        // A hardware refresh must not rebuild the source while WinUI is moving
        // an item through its temporary Remove/Add collection notifications.
        if (!dragging && !Ids.SequenceEqual(desired))
        {
            string? selected = (MetricList.SelectedItem as MetricItem)?.Id;
            items.Clear();
            foreach (string id in desired) items.Add(new MetricItem(id, Move));
            MetricList.SelectedItem = items.FirstOrDefault(item => item.Id == selected) ?? items.FirstOrDefault();
        }
        int columns = PinnedMetricOrder.Columns(settings.MonitorLayout, visible.Length);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var reading = snapshot.Sensors.FirstOrDefault(sensor => sensor.Id == item.Id);
            int index = Array.FindIndex(visible, sensor => sensor.Id == item.Id);
            string position = index < 0 ? L.T("Not currently detected") : L.F("Row {0}, column {1}", index / columns + 1, index % columns + 1);
            string name = settings.SensorNames.GetValueOrDefault(item.Id, reading?.DisplayName ?? L.T("Not currently detected"));
            item.Update(i, items.Count, name, SensorFormatting.Value(reading, snapshot.IsFresh), position,
                settings.SensorColors.GetValueOrDefault(item.Id, settings.MonitorColor));
        }
        empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Preview.Update(settings, config);
    }

    internal void Move(string id, int direction)
    {
        if (dragging) return;
        var item = items.FirstOrDefault(item => item.Id == id);
        if (item is null) return;
        int from = items.IndexOf(item), to = from + direction;
        if (to < 0 || to >= items.Count) return;
        items.Move(from, to); MetricList.SelectedItem = item;
        Commit();
    }

    private void Commit()
    {
        PinnedMetricOrder.TryReorder(settings, Ids, monitorSensors().Select(sensor => sensor.Id));
        changed(); // Also reconcile a cancelled or invalid collection change.
    }

    internal sealed class MetricItem : Grid
    {
        internal string Id { get; }
        internal TextBlock NameText { get; } = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14 };
        internal TextBlock ValueText { get; } = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Margin = new(4, 0, 0, 0) };
        internal TextBlock PositionText { get; } = new() { FontSize = 11 };
        private readonly TextBlock number = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        private readonly Border swatch = new() { Width = 9, Height = 9, CornerRadius = new(4.5), VerticalAlignment = VerticalAlignment.Center };
        private readonly Button up, down;

        internal MetricItem(string id, Action<string, int> move)
        {
            Id = id; MinHeight = 58; ColumnSpacing = 10; Padding = new(0, 4, 0, 4);
            foreach (var width in new[] { GridLength.Auto, new GridLength(20), new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
                ColumnDefinitions.Add(new() { Width = width });
            var grip = new Grid { Width = 12, Height = 20, RowSpacing = 3, ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 3; i++) grip.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < 2; i++) grip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            for (int i = 0; i < 6; i++)
            {
                var dot = new Ellipse { Width = 3, Height = 3, Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
                SetRow(dot, i / 2); SetColumn(dot, i % 2); grip.Children.Add(dot);
            }
            Children.Add(grip); SetColumn(number, 1); Children.Add(number);
            var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(NameText); labels.Children.Add(PositionText); SetColumn(labels, 2); Children.Add(labels);
            var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            swatch.BorderBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]; swatch.BorderThickness = new(1);
            value.Children.Add(swatch); value.Children.Add(ValueText); SetColumn(value, 3); Children.Add(value);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            Button ActionButton(string title, string glyph, int direction)
            {
                var button = new Button
                {
                    Content = new FontIcon { Glyph = glyph, FontSize = 12 }, Width = 30, Height = 32, Padding = new(0),
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"]
                };
                ToolTipService.SetToolTip(button, title); AutomationProperties.SetName(button, title);
                button.Click += (_, _) => move(id, direction);
                return button;
            }
            up = ActionButton(L.T("Move up"), "\uE70E", -1); down = ActionButton(L.T("Move down"), "\uE70D", 1);
            actions.Children.Add(up); actions.Children.Add(down); SetColumn(actions, 4); Children.Add(actions);
            ToolTipService.SetToolTip(grip, L.T("Drag to reorder"));
        }

        internal void Update(int index, int count, string name, string value, string position, string color)
        {
            number.Text = (index + 1).ToString(); NameText.Text = name; ValueText.Text = value; PositionText.Text = position;
            ToolTipService.SetToolTip(NameText, name + "\n" + Id);
            AutomationProperties.SetName(this, L.F("{0}. {1}. {2}", index + 1, name, position));
            up.IsEnabled = index > 0; down.IsEnabled = index + 1 < count;
            if (ScreenColor.TryParse(color, out var parsed))
                swatch.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, parsed.R, parsed.G, parsed.B));
        }
    }
}
