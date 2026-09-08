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
    public string MetricDisplay => InterfaceMetric > 0 ? $"metric {InterfaceMetric}" : "";
    public bool HasMac => !string.IsNullOrEmpty(MacAddress);

    // "Connected" is the one status that means the adapter is actually carrying a
    // network; everything else NetConnectionStatus reports is some flavour of not.
    public bool IsConnected => Status == "Connected";

    private bool _simpleView;

    // Simple view strips the row back to the connection name and its toggle. The flag
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
            Notify(nameof(ShowDetails));
            Notify(nameof(ShowIpRow));
            Notify(nameof(ShowDnsRow));
        }
    }

    public bool ShowDetails => !SimpleView;
    public bool ShowIpRow => !SimpleView && HasIp;
    public bool ShowDnsRow => !SimpleView && HasDnsSuffix;

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
}
