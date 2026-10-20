#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace QuickNetSwitcher;

// The activity log, "Register now", and firewall logging.
//
// Everything here that runs in the background is tied to _shutdown. Closing the window
// or exiting from the tray cancels it and stops the log timer, so none of this can hold
// the app open: the DNS check stops waiting, the log file is never left open between
// reads, and the helpers it launches are short-lived with their own timeouts.
public partial class MainWindow
{
    private const int MaxFirewallLogRows = 500;

    private readonly ActivityLog _activity = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ObservableCollection<FirewallLogEntry> _firewallLog = new();
    private readonly DispatcherTimer _firewallTailTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ICollectionView? _firewallLogView;
    private FirewallLogTail? _firewallTail;
    private bool _firewallTailInFlight;
    private bool _dnsRegisterRunning;
    private readonly Dictionary<string, HashSet<string>> _hiddenValues = new();

    private void InitLogging()
    {
        _activity.CollectionChanged += OnActivityChanged;

        FirewallLogGrid.ItemsSource = _firewallLog;
        _firewallLogView = CollectionViewSource.GetDefaultView(_firewallLog);
        _firewallLogView.Filter = IsFirewallEntryVisible;

        _firewallTailTimer.Tick += (_, _) => _ = ReadFirewallLogAsync();
        IsVisibleChanged += (_, _) => UpdateFirewallTailState();

        InitHits();

        // Covers the tray's Exit, which shuts the app down without a normal close.
        Dispatcher.ShutdownStarted += (_, _) => StopBackgroundWork();
    }

    private void StopBackgroundWork()
    {
        _firewallTailTimer.Stop();
        _hitsTimer.Stop();
        if (!_shutdown.IsCancellationRequested)
            _shutdown.Cancel();
    }

    // --- Activity log ---

    private void ShowActivityLog()
    {
        SetActivityVisible(true);
        ActivityText.ScrollToEnd();
    }

    private void ToggleActivityLog_Click(object sender, RoutedEventArgs e)
    {
        if (ActivityPane.Visibility == Visibility.Visible)
            SetActivityVisible(false);
        else
            ShowActivityLog();
    }

    private void CloseActivityLog_Click(object sender, RoutedEventArgs e) => SetActivityVisible(false);

    private void ClearActivityLog_Click(object sender, RoutedEventArgs e) => _activity.Clear();

    // Mirrors the activity log into the text box: one paragraph per line, appended,
    // trimmed from the top when the log drops its oldest line, emptied on Clear.
    private void OnActivityChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var blocks = ActivityText.Document.Blocks;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (LogLine line in e.NewItems!)
                {
                    var run = new Run(line.Display);
                    if (line.IsError)
                        run.SetResourceReference(TextElement.ForegroundProperty, "ErrorBrush");
                    blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
                }
                Dispatcher.BeginInvoke(DispatcherPriority.Background, ActivityText.ScrollToEnd);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (blocks.FirstBlock != null)
                    blocks.Remove(blocks.FirstBlock);
                break;
            case NotifyCollectionChangedAction.Reset:
                blocks.Clear();
                break;
        }
    }

    private void SetActivityVisible(bool show)
    {
        ActivityPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ApplyActivityLayout();
    }

    // The list and the log share the height in proportion (star rows), so the log
    // grows and shrinks with the window, and the splitter between them sets the
    // proportion. While the window sizes itself to the adapter list, star rows would
    // measure the log to its full length, so it takes a fixed height instead.
    private void ApplyActivityLayout()
    {
        var show = ActivityPane.Visibility == Visibility.Visible;
        var fit = SizeToContent == SizeToContent.Height;

        ActivitySplitter.Visibility = show && !fit ? Visibility.Visible : Visibility.Collapsed;
        ContentRow.MinHeight = fit ? 0 : 200;
        if (!ContentRow.Height.IsStar)
            ContentRow.Height = new GridLength(3, GridUnitType.Star);

        if (!show)
            ActivityRow.Height = new GridLength(0);
        else if (fit)
            ActivityRow.Height = new GridLength(160);
        else if (!ActivityRow.Height.IsStar)
            ActivityRow.Height = new GridLength(1, GridUnitType.Star);
    }

    private void CopyActivityLog_Click(object sender, RoutedEventArgs e) =>
        CopyText(_activity.AsText(), "Activity log copied");

    // WPF's Clipboard.SetText gives up the moment another program holds the clipboard
    // open -- a clipboard manager, or Remote Desktop's clipboard sharing -- and often
    // reports that failure even though the text was copied. The Windows Forms call
    // retries (10 times, 100 ms apart), and a failure is only reported if the text is
    // really not on the clipboard.
    private void CopyText(string text, string done)
    {
        if (text.Length == 0) return;
        try
        {
            WinForms.Clipboard.SetDataObject(text, true, 10, 100);
            ShowStatus(done);
        }
        catch (ExternalException)
        {
            if (ClipboardHolds(text))
                ShowStatus(done);
            else
                ShowError("Couldn't copy: another program is holding the clipboard");
        }
    }

    private static bool ClipboardHolds(string text)
    {
        try
        {
            return WinForms.Clipboard.GetText() == text;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    // --- Register now ---

    private async void RegisterDnsNow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdapterViewModel adapter } button || _dnsRegisterRunning)
            return;

        _dnsRegisterRunning = true;
        button.IsEnabled = false;
        ShowActivityLog();
        ShowStatus($"Registering {adapter.Name} in DNS...");

        var name = adapter.Name;
        var ip = adapter.IpAddress;
        var suffix = adapter.DnsSuffix;
        var log = new Progress<LogLine>(_activity.Append);

        // async void: anything thrown here and not caught would end the process.
        try
        {
            var token = _shutdown.Token;
            var ok = await Task.Run(() => DnsRegistrationService.RegisterAndVerify(name, ip, suffix, log, token), token);
            if (!ok)
                SetStatus($"DNS registration had problems for {name}; see the log", "ErrorBrush");
            else
                ShowStatus($"DNS registration finished for {name}");
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
        }
        finally
        {
            _dnsRegisterRunning = false;
            button.IsEnabled = true;
        }
    }

    // --- Firewall logging switches ---

    private void LogDropped_Click(object sender, RoutedEventArgs e) => _ = SetFirewallLoggingAsync(sender, dropped: true);

    private void LogAllowed_Click(object sender, RoutedEventArgs e) => _ = SetFirewallLoggingAsync(sender, dropped: false);

    private async Task SetFirewallLoggingAsync(object sender, bool dropped)
    {
        if (sender is not CheckBox { Tag: FirewallViewModel profile } box)
            return;

        var enable = box.IsChecked == true;
        var what = dropped ? "dropped packets" : "allowed connections";
        var key = profile.ProfileKey;
        box.IsEnabled = false;
        ShowStatus($"{(enable ? "Turning on" : "Turning off")} logging of {what} for {key}...");

        try
        {
            var success = await Task.Run(() => FirewallLoggingService.SetLogging(key, dropped, enable));
            if (success)
            {
                _activity.Append(LogLine.Info($"Firewall · {key}: logging of {what} {(enable ? "on" : "off")}"));
                ShowStatus($"{key} firewall: logging of {what} {(enable ? "on" : "off")}");
                await LoadFirewallAsync();
            }
            else
            {
                ShowError($"Failed to change logging of {what} for {key}");
                box.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, !enable);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
            box.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, !enable);
        }
        finally
        {
            box.IsEnabled = true;
        }
    }

    // --- Firewall log viewer ---

    private bool IsFirewallLogOpen => FirewallLogView.Visibility == Visibility.Visible;

    private void ViewFirewallLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FirewallViewModel { LogFile: { } path } })
            OpenFirewallLog(path);
    }

    private void OpenFirewallLog(string path)
    {
        _firewallTail = new FirewallLogTail(path);
        _firewallLog.Clear();
        FirewallMain.Visibility = Visibility.Collapsed;
        FirewallLogView.Visibility = Visibility.Visible;
        _ = ReadFirewallLogAsync();
        UpdateFirewallTailState();
    }

    private void CloseFirewallLog_Click(object sender, RoutedEventArgs e) => CloseFirewallLog();

    private void CloseFirewallLog()
    {
        FirewallLogView.Visibility = Visibility.Collapsed;
        FirewallMain.Visibility = Visibility.Visible;
        _firewallTail = null;
        _firewallLog.Clear();
        UpdateFirewallTailState();
        ShowFirewallSummary();
    }

    // The log is read only while someone can see the result: the Firewall tab on
    // screen, and then either the viewer (with Follow ticked) or the hits panel.
    private void UpdateFirewallTailState()
    {
        var onScreen = IsVisible && WindowState != WindowState.Minimized
                       && MainTabs.SelectedIndex == 2 && !_shutdown.IsCancellationRequested;
        var follow = onScreen && IsFirewallLogOpen && FwFollow.IsChecked == true;
        var hits = onScreen && !IsFirewallLogOpen;

        if (follow) _firewallTailTimer.Start();
        else _firewallTailTimer.Stop();

        if (hits && !_hitsTimer.IsEnabled)
        {
            _hitsTimer.Start();
            _ = ReadHitsAsync();
        }
        else if (!hits)
        {
            _hitsTimer.Stop();
        }
    }

    private void FirewallFollow_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateFirewallTailState();
        if (FwFollow.IsChecked == true)
            _ = ReadFirewallLogAsync();
    }

    private async Task ReadFirewallLogAsync()
    {
        var tail = _firewallTail;
        if (tail == null || _firewallTailInFlight) return;

        _firewallTailInFlight = true;
        try
        {
            var token = _shutdown.Token;
            var result = await Task.Run(tail.ReadNew, token);

            // The viewer was closed, or another file opened, while this read ran.
            if (!ReferenceEquals(tail, _firewallTail)) return;

            if (result.Missing)
            {
                ShowStatus($"No {tail.FileName} yet; Windows creates it when it first logs a packet");
                return;
            }
            if (result.Restarted)
                _firewallLog.Clear();

            if (_networkMapAt == DateTime.MinValue)
                await RefreshNetworkMapAsync();

            // Newest first: the file's last line goes to the top.
            foreach (var entry in result.NewEntries)
            {
                Place(entry);
                entry.SourceName = _names!.Lookup(entry.SourceIp);
                entry.DestName = _names.Lookup(entry.DestIp);
                _firewallLog.Insert(0, entry);
            }
            while (_firewallLog.Count > MaxFirewallLogRows)
                _firewallLog.RemoveAt(_firewallLog.Count - 1);

            ShowStatus($"Newest {_firewallLog.Count} lines  ·  {tail.FileName} {result.FileSize / 1024.0 / 1024.0:0.0} MB");
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Couldn't read {tail.FileName}: {ex.Message}");
        }
        finally
        {
            _firewallTailInFlight = false;
        }
    }

    private bool IsFirewallEntryVisible(object item)
    {
        if (item is not FirewallLogEntry entry) return false;
        if (entry.IsDropped && FwShowDropped.IsChecked != true) return false;
        if (entry.IsAllowed && FwShowAllowed.IsChecked != true) return false;
        foreach (var (key, hidden) in _hiddenValues)
        {
            if (hidden.Contains(ColumnValue(entry, key))) return false;
        }

        var text = FwFilterBox.Text.Trim();
        return text.Length == 0
               || entry.Source.Contains(text, StringComparison.OrdinalIgnoreCase)
               || entry.Destination.Contains(text, StringComparison.OrdinalIgnoreCase)
               || entry.SourceName.Contains(text, StringComparison.OrdinalIgnoreCase)
               || entry.DestName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void FirewallLogFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _firewallLogView?.Refresh();
    }

    private void CopyFirewallLog_Click(object sender, RoutedEventArgs e)
    {
        // Everything the grid shows -- after the filters, in its current sort order.
        var shown = _firewallLogView?.Cast<FirewallLogEntry>() ?? Enumerable.Empty<FirewallLogEntry>();
        var lines = shown
            .Select(x => $"{x.When:yyyy-MM-dd HH:mm:ss}  {x.Action,-8} {x.Protocol,-5} " +
                         $"{x.Source}{Named(x.SourceName)} → {x.Destination}{Named(x.DestName)}  {x.Interface} {x.Profile}".TrimEnd());
        var text = string.Join(Environment.NewLine, lines);
        CopyText(text, $"Copied {text.Split('\n').Length} lines");
    }

    private static string Named(string name) => name.Length > 0 ? $" ({name})" : "";

    // --- Column filters in the viewer's headers ---

    private static string ColumnValue(FirewallLogEntry e, string key) => key switch
    {
        "Action" => e.Action,
        "Protocol" => e.Protocol,
        "Interface" => e.Interface,
        "Profile" => e.Profile,
        _ => ""
    };

    // A checklist of the values in that column. Unticking one hides its lines.
    private void HeaderFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } button) return;
        e.Handled = true;

        if (!_hiddenValues.TryGetValue(key, out var hidden))
            _hiddenValues[key] = hidden = new HashSet<string>();

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        var all = new MenuItem { Header = new TextBlock { Text = "Show all" } };
        all.Click += (_, _) =>
        {
            hidden.Clear();
            ApplyHeaderFilter(button, key);
        };
        menu.Items.Add(all);
        menu.Items.Add(new Separator());

        var values = _firewallLog.Select(x => ColumnValue(x, key)).Concat(hidden)
            .Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            // A TextBlock header, so an underscore in a name is shown, not taken as
            // a keyboard shortcut.
            var item = new MenuItem
            {
                Header = new TextBlock { Text = value.Length == 0 ? "(none)" : value },
                IsCheckable = true,
                IsChecked = !hidden.Contains(value),
                StaysOpenOnClick = true
            };
            item.Click += (_, _) =>
            {
                if (item.IsChecked) hidden.Remove(value);
                else hidden.Add(value);
                ApplyHeaderFilter(button, key);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ApplyHeaderFilter(Button button, string key)
    {
        button.SetResourceReference(ForegroundProperty,
            _hiddenValues[key].Count > 0 ? "AccentBrush" : "TextSecondaryBrush");
        _firewallLogView?.Refresh();
    }
}
