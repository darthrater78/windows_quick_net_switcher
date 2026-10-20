#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace QuickNetSwitcher;

// The hits panel on the Firewall tab: the last few minutes of the firewall log,
// counted per profile and per source address.
//
// Read on the same terms as the viewer: every 2 seconds, only while the Firewall tab is
// on screen, off the UI thread, and stopped by the same shutdown token and timer stop.
public partial class MainWindow
{
    private static readonly TimeSpan NetworkMapAge = TimeSpan.FromSeconds(30);

    private readonly FirewallHits _hits = new();
    private readonly ObservableCollection<SourceHits> _sources = new();
    private readonly DispatcherTimer _hitsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, FirewallLogTail> _hitsTails = new(StringComparer.OrdinalIgnoreCase);
    private NetworkMap _networkMap = NetworkMap.Empty;
    private DateTime _networkMapAt = DateTime.MinValue;
    private NameResolver? _names;
    private bool _hitsInFlight;
    private bool _mapInFlight;

    private void InitHits()
    {
        HitsGrid.ItemsSource = _sources;
        _names = new NameResolver(OnNameResolved, _shutdown.Token)
        {
            ResolveOutside = _settings.ResolveOutsideNames
        };
        FwResolveNames.IsChecked = _settings.ResolveOutsideNames;
        _hitsTimer.Tick += (_, _) => _ = ReadHitsAsync();
    }

    private IEnumerable<FirewallViewModel> FirewallProfiles =>
        FirewallList.ItemsSource as IEnumerable<FirewallViewModel> ?? Enumerable.Empty<FirewallViewModel>();

    // The interface and profile a hit came in under (NetworkMap explains how).
    private void Place(FirewallLogEntry entry)
    {
        var location = _networkMap.Locate(entry);
        entry.Interface = location?.Interface ?? "";
        entry.Profile = location?.Profile ?? "";
    }

    private async Task RefreshNetworkMapAsync()
    {
        if (_mapInFlight) return;
        _mapInFlight = true;
        try
        {
            var map = await Task.Run(BuildNetworkMap, _shutdown.Token);
            if (map != null)
                _networkMap = map;
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        finally
        {
            // Also after a failure, so a broken WMI is retried every 30 s, not every read.
            _networkMapAt = DateTime.Now;
            _mapInFlight = false;
        }
        UpdateHitsView();
    }

    private static NetworkMap? BuildNetworkMap()
    {
        try
        {
            return NetworkMap.Build();
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task ReadHitsAsync()
    {
        if (_hitsInFlight || IsFirewallLogOpen) return;

        var paths = FirewallProfiles.Where(p => p.CanViewLog).Select(p => p.LogFile!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        HitsEmpty.Visibility = paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HitsGrid.Visibility = paths.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (paths.Count == 0) return;

        foreach (var stale in _hitsTails.Keys.Except(paths, StringComparer.OrdinalIgnoreCase).ToList())
            _hitsTails.Remove(stale);
        foreach (var path in paths)
        {
            if (!_hitsTails.ContainsKey(path))
                _hitsTails[path] = new FirewallLogTail(path);
        }
        var tails = _hitsTails.Values.ToList();

        _hitsInFlight = true;
        try
        {
            if (DateTime.Now - _networkMapAt > NetworkMapAge)
                await RefreshNetworkMapAsync();

            var (entries, error) = await Task.Run(() => ReadAll(tails), _shutdown.Token);
            foreach (var entry in entries)
                Place(entry);
            _hits.Add(entries);
            UpdateHitsView(error);
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        finally
        {
            _hitsInFlight = false;
        }
    }

    private static (List<FirewallLogEntry>, string?) ReadAll(List<FirewallLogTail> tails)
    {
        var entries = new List<FirewallLogEntry>();
        string? error = null;
        foreach (var tail in tails)
        {
            try
            {
                entries.AddRange(tail.ReadNew().NewEntries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error ??= $"Couldn't read {tail.FileName}: {ex.Message}";
            }
        }
        return (entries, error);
    }

    private void UpdateHitsView(string? error = null)
    {
        var counts = _hits.ByProfile();
        foreach (var profile in FirewallProfiles)
        {
            profile.HitsText = counts.TryGetValue(profile.ProfileKey, out var c)
                ? $"Last 5 min: {c.Dropped} dropped · {c.Allowed} allowed"
                : "Last 5 min: no hits";

            if (_networkMapAt != DateTime.MinValue)
            {
                profile.InUseOn = _networkMap.InUse.TryGetValue(profile.ProfileKey, out var networks)
                    ? $"In use on {string.Join(", ", networks)}"
                    : "Not in use on any network";
            }
        }

        // Cleared and refilled in place, so a column the user sorted by stays sorted.
        _sources.Clear();
        foreach (var source in _hits.BySource())
        {
            source.Name = _names!.Lookup(source.Address);
            _sources.Add(source);
        }

        var since = _hits.ClearedAt is { } cleared && DateTime.Now - cleared < FirewallHits.Window
            ? $"since {cleared:HH:mm}"
            : "last 5 minutes";
        HitsTitle.Text = error
                         ?? $"Top sources · {since}{(_hits.Capped ? " · only the newest 20,000 hits counted" : "")}";
        HitsTitle.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            error != null ? "ErrorBrush" : "TextSecondaryBrush");
    }

    private void OnNameResolved(string address, string name)
    {
        foreach (var source in _sources.Where(s => s.Address == address))
            source.Name = name;
        foreach (var entry in _firewallLog)
        {
            if (entry.SourceIp == address) entry.SourceName = name;
            if (entry.DestIp == address) entry.DestName = name;
        }
    }

    private void ClearHits_Click(object sender, RoutedEventArgs e)
    {
        _hits.Clear();
        UpdateHitsView();
    }

    private void OpenViewerFromHits_Click(object sender, RoutedEventArgs e)
    {
        var path = FirewallProfiles.FirstOrDefault(p => p.CanViewLog)?.LogFile;
        if (path != null)
            OpenFirewallLog(path);
        else
            ShowStatus("Turn on logging for a profile first");
    }

    // Outside addresses are looked up only while this is ticked; local ones always are.
    private void ResolveNames_Click(object sender, RoutedEventArgs e)
    {
        var resolve = FwResolveNames.IsChecked == true;
        _names!.ResolveOutside = resolve;
        _settings.ResolveOutsideNames = resolve;
        SaveSettings();

        foreach (var entry in _firewallLog)
        {
            if (entry.SourceName.Length == 0) entry.SourceName = _names.Lookup(entry.SourceIp);
            if (entry.DestName.Length == 0) entry.DestName = _names.Lookup(entry.DestIp);
        }
        foreach (var source in _sources.Where(s => s.Name.Length == 0))
            source.Name = _names.Lookup(source.Address);
    }

    private void ShowFirewallSummary()
    {
        var profiles = FirewallProfiles.ToList();
        if (profiles.Count == 0) return;
        ShowStatus($"{profiles.Count} firewall profiles  ·  {profiles.Count(p => p.IsEnabled)} enabled" +
                   $"  ·  logging on for {profiles.Count(p => p.IsLogging)}");
    }
}
