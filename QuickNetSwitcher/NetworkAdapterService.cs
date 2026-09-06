using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;

namespace QuickNetSwitcher;

public record AdapterInfo(
    string Name,
    string Description,
    string AdapterId,
    string NetworkAdapterType,
    bool IsEnabled,
    string Status,
    string Speed,
    string MacAddress);

public static class NetworkAdapterService
{
    public static List<AdapterInfo> GetAdapters()
    {
        var adapters = new List<AdapterInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True");

        var dotnetAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .ToDictionary(n => n.Description, n => n, StringComparer.OrdinalIgnoreCase);

        foreach (ManagementObject obj in searcher.Get())
        {
            var name = obj["Name"]?.ToString() ?? "Unknown";
            var description = obj["Description"]?.ToString() ?? "";
            var adapterId = obj["DeviceID"]?.ToString() ?? "";
            var adapterType = obj["AdapterType"]?.ToString() ?? "Unknown";
            var netEnabled = obj["NetEnabled"];
            bool isEnabled = netEnabled != null && (bool)netEnabled;
            var mac = obj["MACAddress"]?.ToString() ?? "";

            var status = "Disabled";
            var speed = "";

            if (isEnabled && dotnetAdapters.TryGetValue(description, out var ni))
            {
                status = ni.OperationalStatus switch
                {
                    OperationalStatus.Up => "Connected",
                    OperationalStatus.Down => "Disconnected",
                    _ => ni.OperationalStatus.ToString()
                };

                if (ni.OperationalStatus == OperationalStatus.Up && ni.Speed > 0)
                {
                    speed = ni.Speed >= 1_000_000_000
                        ? $"{ni.Speed / 1_000_000_000.0:F1} Gbps"
                        : $"{ni.Speed / 1_000_000.0:F0} Mbps";
                }
            }

            adapters.Add(new AdapterInfo(
                name, description, adapterId, adapterType,
                isEnabled, status, speed, mac));
        }

        return adapters.OrderByDescending(a => a.IsEnabled)
                       .ThenBy(a => a.Name)
                       .ToList();
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
}
