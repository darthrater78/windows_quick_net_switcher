#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace QuickNetSwitcher;

public class AdapterViewModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string AdapterId { get; set; } = "";
    public string InterfaceIndex { get; set; } = "";
    public string NetworkAdapterType { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string Status { get; set; } = "";
    public string Speed { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string SubnetCidr { get; set; } = "";
    public string DefaultGateway { get; set; } = "";
    public int InterfaceMetric { get; set; }
    public string DnsSuffix { get; set; } = "";
    public bool RegistersInDns { get; set; }
    public bool RegistersSuffix { get; set; }
    public bool DhcpEnabled { get; set; }
    public bool DnsIsStatic { get; set; }
    public IReadOnlyList<string> DnsServers { get; set; } = Array.Empty<string>();
    public int Ipv4Count { get; set; }

    // Where the address comes from, in words, for the IP line of the details.
    public string IpSourceDisplay => !HasIp ? "" : DhcpEnabled ? "from DHCP" : "static";

    // One value per line of the details.
    public string IpOnly => Ipv4Count > 0 ? IpAddress : "";
    public int PrefixLength => int.TryParse(SubnetCidr.TrimStart('/'), out var p) ? p : 0;
    public string SubnetMaskDisplay => PrefixLength is >= 1 and <= 32
        ? IpSettingsService.ToDotted(IpSettingsService.PrefixToMask(PrefixLength)) : "";
    public string GatewayValue => HasGateway ? DefaultGateway : "none";
    public string DnsServersDisplay => DnsServers.Count > 0 ? string.Join(", ", DnsServers) : "none";
    public string DnsSourceDisplay => DnsServers.Count == 0 ? "" : DnsIsStatic ? "static" : "from DHCP";
    public string MetricValueDisplay => InterfaceMetric > 0 ? InterfaceMetric.ToString() : "auto";

    // The saved presets, read when the row asks which one it matches. Set once by the
    // main window; static so a row needs no reference back to it.
    public static Func<IEnumerable<IpPreset>>? PresetSource { get; set; }

    // A ZTNA client's tunnel adapter: a single /32 address and no gateway (user's rule,
    // 2026-10-31). Its settings belong to the client, so the row shows a fixed ZTNA
    // tag instead of presets and offers no editing.
    public bool IsZtna => HasIp && Ipv4Count > 0 && PrefixLength == 32 && !HasGateway;
    public bool CanEditIp => HasIp && Ipv4Count > 0 && !IsZtna;

    // The tag on the right of the row: the name of the saved preset these settings
    // match, else "DHCP" for plain DHCP, else "Custom".
    public string PresetLabel
    {
        get
        {
            if (!CanEditIp) return "";
            var current = CurrentIpConfig();
            var match = PresetSource?.Invoke().FirstOrDefault(p => p.ToConfig().SameAs(current));
            if (match != null) return match.Name;
            return current.SameAs(IpConfig.Automatic) ? "DHCP" : "Custom";
        }
    }

    // A saved preset gets the solid tag; DHCP and Custom the quiet dashed one.
    public bool IsNamedPreset => PresetLabel is { Length: > 0 } l && l != "DHCP" && l != "Custom"
        || (PresetSource?.Invoke().Any(p => p.Name == PresetLabel) ?? false);

    // Called when presets are saved or deleted: the match may have changed.
    public void RefreshPreset()
    {
        Notify(nameof(PresetLabel));
        Notify(nameof(IsNamedPreset));
    }

    // What the IP settings dialog opens with, and what a revert goes back to. DNS
    // from the network is only possible on a DHCP address (see IpConfig).
    public IpConfig CurrentIpConfig()
    {
        var prefix = int.TryParse(SubnetCidr.TrimStart('/'), out var p) ? p : 0;
        var address = Ipv4Count > 0 ? IpAddress : "";
        return new IpConfig(DhcpEnabled, address, prefix, DefaultGateway,
            DhcpEnabled && !DnsIsStatic, DnsServers.ToList());
    }
    // "Register now" only makes sense for an adapter Windows will register.
    public bool CanRegisterNow => RegistersInDns && HasIp;
    public bool HasSpeed => !string.IsNullOrEmpty(Speed);
    public bool HasIp => !string.IsNullOrEmpty(IpAddress);
    public string IpDisplay => HasIp ? $"{IpAddress}{SubnetCidr}" : "";
    // The same address as the link that opens the presets menu.
    public string IpMenuDisplay => HasIp ? $"{IpDisplay} ▾" : "";
    public string GatewayDisplay => !string.IsNullOrEmpty(DefaultGateway) ? $"gw {DefaultGateway}" : "";
    public bool HasGateway => !string.IsNullOrEmpty(DefaultGateway);
    public bool HasDnsSuffix => !string.IsNullOrEmpty(DnsSuffix);
    public bool HasMetric => InterfaceMetric > 0;
    // Zero means Windows picks the metric itself; the label still has to say something,
    // because it is also the link that opens the metric dialog.
    public string MetricDisplay => InterfaceMetric > 0 ? $"metric {InterfaceMetric}" : "metric auto";
    public bool HasMac => !string.IsNullOrEmpty(MacAddress);

    // "Connected" is the one status that means the adapter is actually carrying a
    // network; everything else NetConnectionStatus reports is some flavour of not.
    public bool IsConnected => Status == "Connected";

    // Mid-transition, on its way to connected or away from it.
    private bool IsBusy =>
        Status is "Connecting" or "Disconnecting" or "Authenticating" or "Authentication succeeded";

    // The thirteen NetConnectionStatus values collapse into the four states a row can
    // actually show. The status dot used to trigger off the status word itself, which
    // had a case for three of them -- an unplugged cable ("Media disconnected") or a
    // failed authentication fell through to the "unknown" colour rather than reading as
    // a disconnection.
    public string StatusKind =>
        !IsEnabled ? "Disabled"
        : IsConnected ? "Connected"
        : IsBusy ? "Working"
        : "Disconnected";

    private bool _simpleView;
    private bool _isExpanded;

    // Simple view keeps each row to its name, status, address and switch. The flag
    // lives on the item rather than the window so the template binds to its own
    // DataContext -- no RelativeSource walk out to the Window, and no extra converters.
    public bool SimpleView
    {
        get => _simpleView;
        set
        {
            if (_simpleView == value) return;
            _simpleView = value;
            Notify(nameof(SimpleView));
            Notify(nameof(DetailsOpen));
        }
    }

    // One row opened by hand while the rest stay in simple view.
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Notify(nameof(IsExpanded));
            Notify(nameof(DetailsOpen));
        }
    }

    public bool DetailsOpen => !SimpleView || IsExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public static AdapterViewModel FromInfo(AdapterInfo info) => new()
    {
        Name = info.Name,
        Description = info.Description,
        AdapterId = info.AdapterId,
        InterfaceIndex = info.InterfaceIndex,
        NetworkAdapterType = info.NetworkAdapterType,
        IsEnabled = info.IsEnabled,
        Status = info.Status,
        Speed = info.Speed,
        MacAddress = info.MacAddress,
        IpAddress = info.IpAddress,
        SubnetCidr = info.SubnetCidr,
        DefaultGateway = info.DefaultGateway,
        InterfaceMetric = info.InterfaceMetric,
        DnsSuffix = info.DnsSuffix,
        RegistersInDns = info.RegistersInDns,
        RegistersSuffix = info.RegistersSuffix,
        DhcpEnabled = info.DhcpEnabled,
        DnsIsStatic = info.DnsIsStatic,
        DnsServers = info.DnsServers,
        Ipv4Count = info.Ipv4Count
    };

    // Re-reads every field from a fresh WMI snapshot, in place, so the row keeps its
    // identity across a refresh instead of being destroyed and rebuilt. Returns
    // whether anything actually moved -- the caller uses that to decide whether the
    // list view needs re-filtering, which matters when this runs unattended every few
    // seconds.
    //
    // A refresh replaces the whole record, so rather than notify field by field this
    // raises one empty-name change: WPF's signal that every binding on the item should
    // re-read, derived properties included. Nothing else mutates these fields, which
    // is what keeps plain auto-properties honest here.
    public bool UpdateFrom(AdapterInfo info)
    {
        if (Matches(info)) return false;

        Name = info.Name;
        Description = info.Description;
        InterfaceIndex = info.InterfaceIndex;
        NetworkAdapterType = info.NetworkAdapterType;
        IsEnabled = info.IsEnabled;
        Status = info.Status;
        Speed = info.Speed;
        MacAddress = info.MacAddress;
        IpAddress = info.IpAddress;
        SubnetCidr = info.SubnetCidr;
        DefaultGateway = info.DefaultGateway;
        InterfaceMetric = info.InterfaceMetric;
        DnsSuffix = info.DnsSuffix;
        RegistersInDns = info.RegistersInDns;
        RegistersSuffix = info.RegistersSuffix;
        DhcpEnabled = info.DhcpEnabled;
        DnsIsStatic = info.DnsIsStatic;
        DnsServers = info.DnsServers;
        Ipv4Count = info.Ipv4Count;

        Notify(string.Empty);
        return true;
    }

    private bool Matches(AdapterInfo info) =>
        Name == info.Name
        && Description == info.Description
        && InterfaceIndex == info.InterfaceIndex
        && NetworkAdapterType == info.NetworkAdapterType
        && IsEnabled == info.IsEnabled
        && Status == info.Status
        && Speed == info.Speed
        && MacAddress == info.MacAddress
        && IpAddress == info.IpAddress
        && SubnetCidr == info.SubnetCidr
        && DefaultGateway == info.DefaultGateway
        && InterfaceMetric == info.InterfaceMetric
        && DnsSuffix == info.DnsSuffix
        && RegistersInDns == info.RegistersInDns
        && RegistersSuffix == info.RegistersSuffix
        && DhcpEnabled == info.DhcpEnabled
        && DnsIsStatic == info.DnsIsStatic
        && DnsServers.SequenceEqual(info.DnsServers)
        && Ipv4Count == info.Ipv4Count;
}
