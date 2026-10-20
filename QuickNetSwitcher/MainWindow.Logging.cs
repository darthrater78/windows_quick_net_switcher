#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

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

    private void InitLogging()
    {
        ActivityList.ItemsSource = _activity;
        _activity.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, ActivityScroll.ScrollToEnd);

        FirewallLogGrid.ItemsSource = _firewallLog;
        _firewallLogView = CollectionViewSource.GetDefaultView(_firewallLog);
        _firewallLogView.Filter = IsFirewallEntryVisible;

        _firewallTailTimer.Tick += (_, _) => _ = ReadFirewallLogAsync();
        IsVisibleChanged += (_, _) => UpdateFirewallTailState();

        // Covers the tray's Exit, which shuts the app down without a normal close.
        Dispatcher.ShutdownStarted += (_, _) => StopBackgroundWork();
    }

    private void StopBackgroundWork()
    {
        _firewallTailTimer.Stop();
        if (!_shutdown.IsCancellationRequested)
            _shutdown.Cancel();
    }

    // --- Activity log ---

    private void ShowActivityLog()
    {
        ActivityPane.Visibility = Visibility.Visible;
        ActivityScroll.ScrollToEnd();
    }

    private void ToggleActivityLog_Click(object sender, RoutedEventArgs e)
    {
        if (ActivityPane.Visibility == Visibility.Visible)
            ActivityPane.Visibility = Visibility.Collapsed;
        else
            ShowActivityLog();
    }

    private void CloseActivityLog_Click(object sender, RoutedEventArgs e) =>
        ActivityPane.Visibility = Visibility.Collapsed;

    private void CopyActivityLog_Click(object sender, RoutedEventArgs e) =>
        CopyText(_activity.AsText(), "Activity log copied");

    private void CopyText(string text, string done)
    {
        if (text.Length == 0) return;
        try
        {
            Clipboard.SetText(text);
            ShowStatus(done);
        }
        catch (COMException)
        {
            // Another program has the clipboard open.
            ShowError("Couldn't copy: the clipboard is busy");
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
        if (sender is not Button { Tag: FirewallViewModel { LogFile: { } path } })
            return;

        _firewallTail = new FirewallLogTail(path);
        _firewallLog.Clear();
        FirewallList.Visibility = Visibility.Collapsed;
        FirewallLogView.Visibility = Visibility.Visible;
        _ = ReadFirewallLogAsync();
        UpdateFirewallTailState();
    }

    private void CloseFirewallLog_Click(object sender, RoutedEventArgs e) => CloseFirewallLog();

    private void CloseFirewallLog()
    {
        FirewallLogView.Visibility = Visibility.Collapsed;
        FirewallList.Visibility = Visibility.Visible;
        _firewallTail = null;
        _firewallLog.Clear();
        UpdateFirewallTailState();
    }

    // Follows the file only while someone can see it: the viewer open, on the Firewall
    // tab, the window on screen, and Follow ticked.
    private void UpdateFirewallTailState()
    {
        var follow = IsVisible && WindowState != WindowState.Minimized
                     && MainTabs.SelectedIndex == 2 && IsFirewallLogOpen
                     && FwFollow.IsChecked == true && !_shutdown.IsCancellationRequested;

        if (follow) _firewallTailTimer.Start();
        else _firewallTailTimer.Stop();
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

            // Newest first: the file's last line goes to the top.
            foreach (var entry in result.NewEntries)
                _firewallLog.Insert(0, entry);
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

        var text = FwFilterBox.Text.Trim();
        return text.Length == 0
               || entry.Source.Contains(text, StringComparison.OrdinalIgnoreCase)
               || entry.Destination.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void FirewallLogFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _firewallLogView?.Refresh();
    }

    private void CopyFirewallLog_Click(object sender, RoutedEventArgs e)
    {
        var lines = FirewallLogGrid.SelectedItems.OfType<FirewallLogEntry>()
            .Select(x => $"{x.Time}  {x.Action,-8} {x.Protocol,-5} {x.Source} → {x.Destination}  {x.Direction}");
        CopyText(string.Join(Environment.NewLine, lines), "Copied the selected lines");
    }
}
