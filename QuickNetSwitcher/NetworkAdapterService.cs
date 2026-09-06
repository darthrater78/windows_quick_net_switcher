using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

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

        foreach (ManagementObject obj in searcher.Get())
        {
            var connectionId = obj["NetConnectionID"]?.ToString();
            var hardwareName = obj["Name"]?.ToString() ?? "Unknown";
            var name = !string.IsNullOrEmpty(connectionId) ? connectionId : hardwareName;
            var description = obj["Description"]?.ToString() ?? "";
            var adapterId = obj["DeviceID"]?.ToString() ?? "";
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
            else if (!isEnabled)
            {
                status = "Disabled";
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
