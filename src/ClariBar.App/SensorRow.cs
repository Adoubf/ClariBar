using System.ComponentModel;
using ClariBar.Core;

namespace ClariBar.App;

public sealed class SensorRow : INotifyPropertyChanged
{
    public SensorReading Reading { get; private set; }
    private string alias;
    private bool fresh;
    public string Id => Reading.Id;
    public string Name => string.IsNullOrWhiteSpace(alias) ? Reading.DisplayName : alias;
    public string Details => $"{Reading.Name}\n{Reading.Id}";
    public string Device => Reading.Hardware;
    public string Kind => SensorLabels.Kind(Reading.Kind);
    public string Value => SensorFormatting.Value(Reading, fresh);
    public string Status => fresh ? Reading.StatusText : "数据过期";
    public event PropertyChangedEventHandler? PropertyChanged;
    public SensorRow(SensorReading reading, string customName, bool isFresh)
    { Reading = reading; alias = customName; fresh = isFresh; }
    public void Update(SensorReading reading, string customName, bool isFresh)
    {
        Reading = reading; alias = customName; fresh = isFresh;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
