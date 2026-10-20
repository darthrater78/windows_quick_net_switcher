#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace QuickNetSwitcher;

// One row of the hits panel: a source address and what it did in the window.
public sealed class SourceHits : INotifyPropertyChanged
{
    public string Address { get; init; } = "";
    public int Dropped { get; init; }
    public int Allowed { get; init; }
    public int Total => Dropped + Allowed;
    public string Profile { get; init; } = "";
    public string Interface { get; init; } = "";
    public string Ports { get; init; } = "";

    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public record ProfileHits(int Dropped, int Allowed);

// The last few minutes of firewall hits, kept in memory for the hits panel. Lives on
// the UI thread. Capped, so a busy machine logging every allowed connection cannot
// grow it without bound.
public sealed class FirewallHits
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private const int MaxHits = 20000;
    private const int TopSources = 15;
    private const int MaxPortsShown = 4;

    private readonly Queue<FirewallLogEntry> _hits = new();

    public DateTime? ClearedAt { get; private set; }
    public bool Capped { get; private set; }

    public void Add(IEnumerable<FirewallLogEntry> entries)
    {
        foreach (var e in entries)
        {
            if (ClearedAt is { } cleared && e.When < cleared) continue;
            _hits.Enqueue(e);
        }
        Trim();
    }

    public void Clear()
    {
        _hits.Clear();
        ClearedAt = DateTime.Now;
        Capped = false;
    }

    private void Trim()
    {
        var cutoff = DateTime.Now - Window;
        while (_hits.Count > 0 && _hits.Peek().When < cutoff)
            _hits.Dequeue();
        while (_hits.Count > MaxHits)
        {
            _hits.Dequeue();
            Capped = true;
        }
    }

    public Dictionary<string, ProfileHits> ByProfile()
    {
        Trim();
        return _hits
            .Where(h => h.Profile.Length > 0)
            .GroupBy(h => h.Profile)
            .ToDictionary(g => g.Key, g => new ProfileHits(g.Count(h => h.IsDropped), g.Count(h => h.IsAllowed)));
    }

    // Busiest sources first; the grid re-sorts by whatever header is clicked.
    public List<SourceHits> BySource()
    {
        Trim();
        return _hits
            .GroupBy(h => h.SourceIp)
            .Select(g => new SourceHits
            {
                Address = g.Key,
                Dropped = g.Count(h => h.IsDropped),
                Allowed = g.Count(h => h.IsAllowed),
                Profile = MostCommon(g.Select(h => h.Profile)),
                Interface = MostCommon(g.Select(h => h.Interface)),
                Ports = Ports(g)
            })
            .OrderByDescending(s => s.Total)
            .Take(TopSources)
            .ToList();
    }

    private static string MostCommon(IEnumerable<string> values) =>
        values.Where(v => v.Length > 0).GroupBy(v => v).OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefault() ?? "";

    private static string Ports(IEnumerable<FirewallLogEntry> hits)
    {
        var parts = hits
            .Where(h => h.DestPort is not ("-" or ""))
            .GroupBy(h => (h.Protocol, h.DestPort))
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();
        var shown = parts.Take(MaxPortsShown)
            .GroupBy(p => p.Protocol)
            .Select(g => $"{g.Key} {string.Join(", ", g.Select(p => p.DestPort))}");
        var text = string.Join(" · ", shown);
        return parts.Count > MaxPortsShown ? $"{text} +{parts.Count - MaxPortsShown}" : text;
    }
}
