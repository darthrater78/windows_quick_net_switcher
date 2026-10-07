#nullable enable
using System.ComponentModel;

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
    public bool HasSpeed => !string.IsNullOrEmpty(Speed);
    public bool HasIp => !string.IsNullOrEmpty(IpAddress);
    public string IpDisplay => HasIp ? $"{IpAddress}{SubnetCidr}" : "";
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
        DnsSuffix = info.DnsSuffix
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
        && DnsSuffix == info.DnsSuffix;
}
