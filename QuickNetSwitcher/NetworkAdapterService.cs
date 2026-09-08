using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;

namespace QuickNetSwitcher;

public record AdapterInfo(
    string Name,
    string Description,
    string AdapterId,
    string InterfaceIndex,
    string NetworkAdapterType,
    bool IsEnabled,
    string Status,
    string Speed,
    string MacAddress,
    string IpAddress,
    string SubnetCidr,
    string DefaultGateway,
    int InterfaceMetric,
    string DnsSuffix);

public static class NetworkAdapterService
{
    public static List<AdapterInfo> GetAdapters()
    {
        var adapters = new List<AdapterInfo>();

        var configMap = new Dictionary<string, (string Ip, string Cidr, string Gateway, int Metric, string DnsSuffix)>();
        using (var cfgSearcher = new ManagementObjectSearcher(
            "SELECT Index, IPAddress, IPSubnet, DefaultIPGateway, IPConnectionMetric, DNSDomain FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True"))
        {
            foreach (ManagementObject cfg in cfgSearcher.Get())
            {
                var index = cfg["Index"]?.ToString() ?? "";
                var ips = cfg["IPAddress"] as string[];
                var subnets = cfg["IPSubnet"] as string[];
                var gateways = cfg["DefaultIPGateway"] as string[];
                var metric = cfg["IPConnectionMetric"] != null ? Convert.ToInt32(cfg["IPConnectionMetric"]) : 0;
                var dnsSuffix = cfg["DNSDomain"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(index) || ips is not { Length: > 0 })
                    continue;

                var ip = ips[0];
                var cidr = "";
                if (subnets is { Length: > 0 })
                    cidr = $"/{MaskToCidr(subnets[0])}";
                var gateway = gateways is { Length: > 0 } ? gateways[0] : "";

                configMap[index] = (ip, cidr, gateway, metric, dnsSuffix);
            }
        }

        var dnsFallback = new Dictionary<string, string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = ni.GetIPProperties();
                if (!string.IsNullOrEmpty(props.DnsSuffix))
                    dnsFallback[ni.Id] = props.DnsSuffix;
            }
        }
        catch { }

        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True");

        foreach (ManagementObject obj in searcher.Get())
        {
            var connectionId = obj["NetConnectionID"]?.ToString();
            var hardwareName = obj["Name"]?.ToString() ?? "Unknown";
            var name = !string.IsNullOrEmpty(connectionId) ? connectionId : hardwareName;
            var description = obj["Description"]?.ToString() ?? "";
            var adapterId = obj["DeviceID"]?.ToString() ?? "";
            var interfaceIndex = obj["InterfaceIndex"]?.ToString() ?? "";
            var adapterType = obj["AdapterType"]?.ToString() ?? "Unknown";
            var netEnabled = obj["NetEnabled"];
            bool isEnabled = netEnabled != null && (bool)netEnabled;
            var mac = obj["MACAddress"]?.ToString() ?? "";

            var statusCode = obj["NetConnectionStatus"];
            var status = "Disabled";
            var speed = "";

            if (isEnabled && statusCode != null)
            {
                status = Convert.ToInt32(statusCode) switch
                {
                    0 => "Disconnected",
                    1 => "Connecting",
                    2 => "Connected",
                    3 => "Disconnecting",
                    4 => "Hardware not present",
                    5 => "Hardware disabled",
                    6 => "Hardware malfunction",
                    7 => "Media disconnected",
                    8 => "Authenticating",
                    9 => "Authentication succeeded",
                    10 => "Authentication failed",
                    11 => "Invalid address",
                    12 => "Credentials required",
                    _ => "Unknown"
                };
            }

            if (isEnabled && statusCode != null && Convert.ToInt32(statusCode) == 2)
            {
                var adapterSpeed = obj["Speed"];
                if (adapterSpeed != null)
                {
                    long speedBps = Convert.ToInt64(adapterSpeed);
                    speed = speedBps >= 1_000_000_000
                        ? $"{speedBps / 1_000_000_000.0:F1} Gbps"
                        : $"{speedBps / 1_000_000.0:F0} Mbps";
                }
            }

            configMap.TryGetValue(adapterId, out var netConfig);

            var dnsSuffix = netConfig.DnsSuffix ?? "";
            if (string.IsNullOrEmpty(dnsSuffix))
            {
                var guid = obj["GUID"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(guid))
                    dnsFallback.TryGetValue(guid, out dnsSuffix!);
                dnsSuffix ??= "";
            }

            adapters.Add(new AdapterInfo(
                name, description, adapterId, interfaceIndex, adapterType,
                isEnabled, status, speed, mac,
                netConfig.Ip ?? "", netConfig.Cidr ?? "", netConfig.Gateway ?? "",
                netConfig.Metric, dnsSuffix));
        }

        return adapters.OrderBy(a => a.Name).ToList();
    }

    public static bool SetAdapterState(string adapterId, bool enable)
    {
        if (!int.TryParse(adapterId, out _))
            return false;

        using var searcher = new ManagementObjectSearcher(
            $"SELECT * FROM Win32_NetworkAdapter WHERE DeviceID = '{adapterId}'");

        foreach (ManagementObject obj in searcher.Get())
        {
            var methodName = enable ? "Enable" : "Disable";
            var result = obj.InvokeMethod(methodName, null);
            return result != null && Convert.ToInt32(result) == 0;
        }

        return false;
    }

    public static bool SetInterfaceMetric(string interfaceAlias, int metric)
    {
        if (metric < 1 || metric > 9999 || string.IsNullOrEmpty(interfaceAlias))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = SystemPaths.Netsh,
            Arguments = $"interface ipv4 set interface \"{interfaceAlias}\" metric={metric}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return false;
        proc.WaitForExit(5000);
        return proc.ExitCode == 0;
    }

    private static int MaskToCidr(string mask)
    {
        if (!System.Net.IPAddress.TryParse(mask, out var ip))
            return 0;
        var bytes = ip.GetAddressBytes();
        int bits = 0;
        foreach (var b in bytes)
        {
            byte val = b;
            while (val != 0)
            {
                bits += val & 1;
                val >>= 1;
            }
        }
        return bits;
    }
}
