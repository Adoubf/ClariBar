namespace ClariBar.Core;

public static class PinnedMetricOrder
{
    public static bool TryReorder(AppSettings settings, IEnumerable<string> proposed, IEnumerable<string> defaults)
    {
        string[] current = settings.PinnedSensors.Length > 0 ? settings.PinnedSensors : defaults.Take(8).ToArray();
        string[] next = proposed.ToArray();
        // WinUI reordering can emit Remove followed by Add. Only a complete
        // permutation may be saved; never persist an intermediate removal.
        if (next.Length != current.Length || next.Distinct().Count() != next.Length ||
            !current.ToHashSet().SetEquals(next) || current.SequenceEqual(next)) return false;
        settings.PinnedSensors = next;
        return true;
    }

    public static int Columns(string layout, int count) => layout switch
    {
        "TwoColumns" => 2,
        "TwoRows" => Math.Max(1, (count + 1) / 2),
        _ => Math.Max(1, count)
    };
}
