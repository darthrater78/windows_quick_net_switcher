#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace QuickNetSwitcher;

// Edit IP settings: DHCP or static address, gateway and DNS for one adapter, with
// saved presets to fill the fields from. The dialog only collects and checks the
// settings; the main window applies them and asks to keep them.
public partial class IpSettingsDialog : Window
{
    private readonly IpConfig _current;
    private readonly List<IpPreset> _presets;
    private readonly Func<bool> _savePresets;
    private IpPreset? _selected;

    public IpConfig? Result { get; private set; }

    public IpSettingsDialog(string adapterName, IpConfig current, int ipv4Count,
        List<IpPreset> presets, Func<bool> savePresets, bool startSaving = false)
    {
        InitializeComponent();
        _current = current;
        _presets = presets;
        _savePresets = savePresets;

        AdapterLabel.Text = $"IP settings — {adapterName}";
        if (ipv4Count > 1)
        {
            MultiAddressText.Text = $"{adapterName} has {ipv4Count} IPv4 addresses. Only the first is shown, and Apply replaces them all.";
            MultiAddressText.Visibility = Visibility.Visible;
        }

        Fill(current);

        if (startSaving)
            ShowSavePanel();
        else
            (current.Dhcp ? (Control)DhcpRadio : AddressInput).Focus();
    }

    private void Fill(IpConfig c)
    {
        AddressInput.Text = c.Dhcp ? "" : c.Address;
        PrefixInput.Text = c.Dhcp || c.Prefix <= 0 ? "" : c.Prefix.ToString();
        GatewayInput.Text = c.Dhcp ? "" : c.Gateway;
        Dns1Input.Text = c.Dns.Count > 0 ? c.Dns[0] : "";
        Dns2Input.Text = c.Dns.Count > 1 ? c.Dns[1] : "";
        DnsAutoCheck.IsChecked = c.DnsAuto;
        // Set last: its Checked handler reads the DNS box above.
        (c.Dhcp ? DhcpRadio : StaticRadio).IsChecked = true;
        UpdateEnabled();
        HideMessage();
    }

    private IpConfig ReadFields()
    {
        var dhcp = DhcpRadio.IsChecked == true;
        var prefix = int.TryParse(PrefixInput.Text.Trim(), out var p) ? p : -1;
        var dns = new[] { Dns1Input.Text.Trim(), Dns2Input.Text.Trim() }
            .Where(d => d.Length > 0)
            .ToList();
        return new IpConfig(dhcp, AddressInput.Text.Trim(), prefix, GatewayInput.Text.Trim(),
            dhcp && DnsAutoCheck.IsChecked == true, dns);
    }

    // Static fields only take input for a static address; DNS from the network is only
    // offered with DHCP, the same rule Windows' own dialog follows.
    private void UpdateEnabled()
    {
        var dhcp = DhcpRadio.IsChecked == true;
        AddressInput.IsEnabled = PrefixInput.IsEnabled = GatewayInput.IsEnabled = !dhcp;
        if (!dhcp) DnsAutoCheck.IsChecked = false;
        DnsAutoCheck.IsEnabled = dhcp;
        var dnsAuto = dhcp && DnsAutoCheck.IsChecked == true;
        Dns1Input.IsEnabled = Dns2Input.IsEnabled = !dnsAuto;
        UpdateMask();
    }

    private void UpdateMask()
    {
        MaskText.Text = PrefixInput.IsEnabled && int.TryParse(PrefixInput.Text.Trim(), out var p) && p is >= 1 and <= 32
            ? IpSettingsService.ToDotted(IpSettingsService.PrefixToMask(p))
            : "";
    }

    private void Source_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdateEnabled();
    }

    private void DnsAuto_Click(object sender, RoutedEventArgs e) => UpdateEnabled();

    private void Prefix_TextChanged(object sender, TextChangedEventArgs e) => UpdateMask();

    // --- Presets -------------------------------------------------------------------

    private void PresetPicker_Click(object sender, RoutedEventArgs e)
    {
        PresetMenu.Items.Clear();
        PresetMenu.Items.Add(MakeItem("Current settings", _current.Dhcp ? "DHCP" : _current.Summary,
            () => Pick(null, _current, "Current settings")));
        PresetMenu.Items.Add(MakeItem("Automatic (DHCP)", IpConfig.Automatic.Summary,
            () => Pick(null, IpConfig.Automatic, "Automatic (DHCP)")));
        if (_presets.Count > 0)
            PresetMenu.Items.Add(new Separator());
        foreach (var preset in _presets)
        {
            var p = preset;
            PresetMenu.Items.Add(MakeItem(p.Name, p.ToConfig().Summary, () => Pick(p, p.ToConfig(), p.Name)));
        }

        PresetMenu.PlacementTarget = PresetPicker;
        PresetMenu.Placement = PlacementMode.Bottom;
        PresetMenu.MinWidth = PresetPicker.ActualWidth;
        PresetMenu.IsOpen = true;
    }

    // A menu entry with the name over a one-line summary of what it sets.
    internal static MenuItem MakeItem(string name, string summary, Action onClick, bool? isChecked = null)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = name });
        var sub = new TextBlock
        {
            Text = summary,
            FontSize = 12,
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("DataFont")
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        header.Children.Add(sub);

        var item = new MenuItem { Header = header };
        if (isChecked != null)
        {
            item.IsCheckable = true;
            item.IsChecked = isChecked.Value;
        }
        item.Click += (_, _) => onClick();
        return item;
    }

    private void Pick(IpPreset? preset, IpConfig config, string label)
    {
        _selected = preset;
        PresetPickerText.Text = label;
        DeletePresetButton.Visibility = preset != null ? Visibility.Visible : Visibility.Collapsed;
        Fill(config);
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var name = _selected.Name;
        _presets.Remove(_selected);
        if (!_savePresets())
        {
            _presets.Add(_selected);
            ShowMessage("Couldn't save the settings file; the preset is still there.", isError: true);
            return;
        }
        _selected = null;
        PresetPickerText.Text = "Current settings";
        DeletePresetButton.Visibility = Visibility.Collapsed;
        ShowMessage($"Deleted preset \"{name}\".", isError: false);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e) => ShowSavePanel();

    private void ShowSavePanel()
    {
        SavePanel.Visibility = Visibility.Visible;
        PresetNameInput.Text = _selected?.Name ?? "";
        PresetNameInput.SelectAll();
        PresetNameInput.Focus();
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var config = ReadFields();
        if (IpSettingsService.Validate(config) is { } problem)
        {
            ShowMessage(problem, isError: true);
            return;
        }

        var name = PresetNameInput.Text.Trim();
        if (IpSettingsService.ValidateName(name, _presets) is { } nameProblem)
        {
            ShowMessage(nameProblem, isError: true);
            PresetNameInput.Focus();
            return;
        }

        // The same name replaces the preset rather than adding a second one.
        var index = _presets.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var preset = IpPreset.From(name, config);
        var previous = index >= 0 ? _presets[index] : null;
        if (index >= 0) _presets[index] = preset; else _presets.Add(preset);

        if (!_savePresets())
        {
            if (previous != null) _presets[index] = previous; else _presets.Remove(preset);
            ShowMessage("Couldn't save the settings file.", isError: true);
            return;
        }

        _selected = preset;
        PresetPickerText.Text = name;
        DeletePresetButton.Visibility = Visibility.Visible;
        SavePanel.Visibility = Visibility.Collapsed;
        ShowMessage(previous != null ? $"Updated preset \"{name}\"." : $"Saved preset \"{name}\".", isError: false);
    }

    // --- Apply ---------------------------------------------------------------------

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var config = ReadFields();
        if (IpSettingsService.Validate(config) is { } problem)
        {
            // Said in the dialog, beside the fields, not in a second window.
            ShowMessage(problem, isError: true);
            return;
        }
        Result = config;
        DialogResult = true;
    }

    private void ShowMessage(string text, bool isError)
    {
        ErrorText.Text = text;
        ErrorText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ErrorBrush" : "TextSecondaryBrush");
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideMessage() => ErrorText.Visibility = Visibility.Collapsed;
}
