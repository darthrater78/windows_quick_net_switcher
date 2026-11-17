#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace QuickNetSwitcher;

// IP settings: the "Edit IP settings…" dialog in a row's details, and the presets menu
// on the row's address. Both end in ApplyIpAsync, which applies, asks to keep, and
// puts the old settings back unless the answer is Keep.
public partial class MainWindow
{
    private bool _ipBusy;

    private void EditIpSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AdapterViewModel adapter })
            OpenIpDialog(adapter, startSaving: false);
    }

    private void IpPresetsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdapterViewModel adapter } button) return;
        if (RefuseZtna(adapter)) return;

        var current = adapter.CurrentIpConfig();
        var menu = new ContextMenu();

        menu.Items.Add(IpSettingsDialog.MakeItem("Automatic (DHCP)", IpConfig.Automatic.Summary,
            () => _ = ApplyIpAsync(adapter, IpConfig.Automatic, "Automatic (DHCP)"),
            current.SameAs(IpConfig.Automatic)));

        foreach (var preset in _settings.IpPresets)
        {
            var p = preset;
            var config = p.ToConfig();
            menu.Items.Add(IpSettingsDialog.MakeItem(p.Name, config.Summary,
                () => _ = ApplyIpAsync(adapter, config, p.Name),
                current.SameAs(config)));
        }

        menu.Items.Add(new Separator());
        var edit = new MenuItem { Header = "Edit IP settings…" };
        edit.Click += (_, _) => OpenIpDialog(adapter, startSaving: false);
        menu.Items.Add(edit);
        var save = new MenuItem { Header = "Save current as preset…" };
        save.Click += (_, _) => OpenIpDialog(adapter, startSaving: true);
        menu.Items.Add(save);

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OpenIpDialog(AdapterViewModel adapter, bool startSaving)
    {
        if (RefuseZtna(adapter)) return;
        var dialog = new IpSettingsDialog(adapter.Name, adapter.CurrentIpConfig(), adapter.Ipv4Count,
            _settings.IpPresets, SavePresets, startSaving)
        {
            Owner = this
        };
        dialog.SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(dialog);

        if (dialog.ShowDialog() == true && dialog.Result is { } config)
            _ = ApplyIpAsync(adapter, config, null);
    }

    // The buttons are hidden on a ZTNA row; this is the check behind them, so no path
    // (a stale menu, a refresh mid-click) can change a tunnel adapter.
    private bool RefuseZtna(AdapterViewModel adapter)
    {
        if (!adapter.IsZtna) return false;
        ShowStatus($"{adapter.Name} is a ZTNA adapter; its IP settings belong to the ZTNA client");
        return true;
    }

    // Saving or deleting a preset can change which tag a row shows.
    private bool SavePresets()
    {
        var ok = SettingsService.Save(_settings);
        foreach (var a in _adapters)
            a.RefreshPreset();
        return ok;
    }

    // Never throws: it is started fire-and-forget from menu clicks.
    private async Task ApplyIpAsync(AdapterViewModel adapter, IpConfig config, string? presetName)
    {
        if (RefuseZtna(adapter)) return;

        if (_ipBusy)
        {
            ShowStatus("Still applying the last IP change...");
            return;
        }

        // Captured now: a refresh while this runs replaces the row's fields.
        var name = adapter.Name;
        var adapterId = adapter.AdapterId;
        var previous = adapter.CurrentIpConfig();
        var register = adapter.RegistersInDns;
        var suffix = adapter.RegistersSuffix;
        var label = presetName != null ? $"\"{presetName}\"" : "IP settings";

        if (config.SameAs(previous))
        {
            ShowStatus($"{name} already has these settings");
            return;
        }

        // Without settings to go back to there is no safety net, so nothing is changed.
        if (IpSettingsService.Validate(previous) != null)
        {
            ShowError($"Couldn't read {name}'s current settings well enough to undo a change; nothing applied");
            return;
        }

        _ipBusy = true;
        // Set once netsh may have touched the adapter: from then on, any way out other
        // than Keep (including an exception) has to put the old settings back.
        var started = false;
        var settled = false;
        try
        {
            ShowStatus($"Applying {label} to {name}...");
            started = true;
            var error = await Task.Run(() => IpSettingsService.Apply(name, previous, config, register, suffix));
            if (error != null)
            {
                ShowError($"Failed to apply {label} to {name}: {error}");
                settled = true;
                await RestoreIpAsync(name, adapterId, previous, register, suffix);
                return;
            }

            _activity.Append(LogLine.Info($"IP · {name}: applied {label} ({Describe(config)})"));
            await Task.Delay(500);
            await LoadAdaptersAsync();

            var keep = new KeepSettingsDialog(name, Describe(config), Describe(previous)) { Owner = this };
            keep.SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(keep);
            if (keep.ShowDialog() == true)
            {
                settled = true;
                ShowStatus($"{name}: kept {label}");
                return;
            }

            settled = true;
            await RestoreIpAsync(name, adapterId, previous, register, suffix);
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
            if (started && !settled)
                await RestoreIpAsync(name, adapterId, previous, register, suffix);
        }
        finally
        {
            _ipBusy = false;
        }
    }

    // Puts `previous` back, starting from what the adapter has now rather than from what
    // was asked for: after a failure part of the change may have landed, part not.
    // Never throws: it also runs from ApplyIpAsync's catch, where nothing is left to
    // catch for it, so a failure is reported as an error instead.
    private async Task RestoreIpAsync(string name, string adapterId, IpConfig previous,
        bool register, bool suffix)
    {
        ShowStatus($"Putting {name}'s previous settings back...");
        var error = await Task.Run(() =>
        {
            try
            {
                var info = NetworkAdapterService.GetAdapters().Find(a => a.AdapterId == adapterId);
                // No reading (adapter gone mid-change): assume nothing matches, so every
                // part is set again.
                var now = info != null
                    ? AdapterViewModel.FromInfo(info).CurrentIpConfig()
                    : new IpConfig(!previous.Dhcp, "", 0, "", !previous.DnsAuto, new[] { "0.0.0.0" });
                return now.SameAs(previous) ? null : IpSettingsService.Apply(name, now, previous, register, suffix);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });

        if (error == null)
        {
            ShowStatus($"{name} is back to {Describe(previous)}");
            _activity.Append(LogLine.Info($"IP · {name}: back to {Describe(previous)}"));
        }
        else
        {
            ShowError($"Couldn't put {name}'s previous settings back ({Describe(previous)}): {error}");
        }
        await Task.Delay(500);
        await LoadAdaptersAsync();
    }

    private static string Describe(IpConfig c) =>
        (c.Dhcp ? "DHCP" : $"{c.Address}/{c.Prefix}")
        + (c.DnsAuto ? "" : c.Dns.Count == 0 ? ", no DNS servers" : $", dns {string.Join(", ", c.Dns)}");
}
