#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace QuickNetSwitcher;

public record LogLine(DateTime Time, string Text, bool IsError = false)
{
    public string Display => $"{Time:HH:mm:ss}  {Text}";

    public static LogLine Info(string text) => new(DateTime.Now, text);
    public static LogLine Error(string text) => new(DateTime.Now, text, true);
}

// The shared activity log above the footer. Capped, so a window left open for weeks
// cannot grow it without bound.
public class ActivityLog : ObservableCollection<LogLine>
{
    private const int MaxLines = 1000;

    public void Append(LogLine line)
    {
        Add(line);
        while (Count > MaxLines)
            RemoveAt(0);
    }

    public string AsText() => string.Join(Environment.NewLine, this.Select(l => l.Display));
}
