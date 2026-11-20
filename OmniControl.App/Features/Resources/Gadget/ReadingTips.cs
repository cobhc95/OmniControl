namespace OmniControl.App.Features.Resources.Gadget;

/// The readings' hover tooltip: for each reading its chosen details ("CPU usage · 3 %", "Today 1 % – 64 %", the small graph, the sensor or adapter, the warn point).
internal static class ReadingTips
{
    public static string DetailName(ReadingDetail detail) => detail switch
    {
        ReadingDetail.FullName => "Full name",
        ReadingDetail.TodayRange => "Today's high / low",
        ReadingDetail.Graph => "Small graph",
        ReadingDetail.Source => "Sensor / adapter",
        ReadingDetail.WarnPoint => "Warn point",
        _ => detail.ToString(),
    };

    public static IReadOnlyList<GadgetTipLine> Lines(ResourceMonitor monitor, IEnumerable<GadgetReading> rows, IReadOnlyCollection<ReadingDetail> shows)
    {
        var lines = new List<GadgetTipLine>();
        foreach (var row in rows)
        {
            if (monitor.Find(row.Id) is not { } reading) continue;
            var part = Of(monitor, row, reading, shows).ToList();
            if (part.Count == 0) continue;
            if (lines.Count > 0) part[0] = part[0] with { StartsPart = true };
            lines.AddRange(part);
        }
        return lines;
    }

    private static IEnumerable<GadgetTipLine> Of(ResourceMonitor monitor, GadgetReading row, Reading reading, IReadOnlyCollection<ReadingDetail> shows)
    {
        var sample = monitor.Latest;
        var head = new[]
        {
            shows.Contains(ReadingDetail.FullName) ? reading.Name : null,
            shows.Contains(ReadingDetail.Value) ? reading.Words(reading.Read(sample)) : null,
        }.OfType<string>().ToList();
        if (head.Count > 0) yield return new GadgetTipLine(string.Join(" · ", head));
        if (shows.Contains(ReadingDetail.TodayRange) && monitor.Today(reading.Id) is { } today)
            yield return new GadgetTipLine($"Today {reading.Words(today.Low)} – {reading.Words(today.High)}", Muted: true);
        if (shows.Contains(ReadingDetail.Graph)) yield return new GadgetTipLine(string.Empty, Graph: monitor.History(reading.Id), GraphMax: reading.GraphMax);
        if (shows.Contains(ReadingDetail.Source) && reading.Source?.Invoke(sample) is { Length: > 0 } source) yield return new GadgetTipLine(source, Muted: true);
        if (shows.Contains(ReadingDetail.WarnPoint) && row.WarnAt is { } at) yield return new GadgetTipLine($"Warns at {reading.UnitWords(at)}", Muted: true);
    }
}
