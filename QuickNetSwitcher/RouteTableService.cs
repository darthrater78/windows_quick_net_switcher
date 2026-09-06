using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace QuickNetSwitcher;

public class RouteEntry
{
    public string Destination { get; set; } = "";
    public string Cidr { get; set; } = "";
    public string DisplayDestination => string.IsNullOrEmpty(Cidr) ? Destination : $"{Destination}{Cidr}";
    public string Gateway { get; set; } = "";
    public string InterfaceIp { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public int Metric { get; set; }
    public string RouteType { get; set; } = "";
}

public static class RouteTableService
{
    public static List<RouteEntry> GetRoutes()
    {
        var ifNameMap = BuildInterfaceNameMap();
        var routes = new List<RouteEntry>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Destination, Mask, NextHop, InterfaceIndex, Metric1 FROM Win32_IP4RouteTable");

        foreach (ManagementObject obj in searcher.Get())
        {
            var dest = obj["Destination"]?.ToString() ?? "";
            var mask = obj["Mask"]?.ToString() ?? "";
            var nextHop = obj["NextHop"]?.ToString() ?? "";
            var ifIndex = obj["InterfaceIndex"]?.ToString() ?? "";
            var metric = Convert.ToInt32(obj["Metric1"] ?? 0);

            var cidr = !string.IsNullOrEmpty(mask) ? $"/{MaskToCidr(mask)}" : "";

            var hasIf = ifNameMap.TryGetValue(ifIndex, out var ifInfo);

            var routeType = ClassifyRoute(dest, mask, nextHop);

            routes.Add(new RouteEntry
            {
                Destination = dest,
                Cidr = cidr,
                Gateway = nextHop,
                InterfaceIp = hasIf ? ifInfo.Ip : "",
                InterfaceName = hasIf ? ifInfo.Name : $"IF {ifIndex}",
                Metric = metric,
                RouteType = routeType
            });
        }

        return routes
            .OrderBy(r => RouteTypePriority(r.RouteType))
            .ThenBy(r => r.Metric)
            .ThenBy(r => r.Destination)
            .ToList();
    }

    private static string ClassifyRoute(string dest, string mask, string gateway)
    {
        if (dest == "0.0.0.0" && mask == "0.0.0.0") return "Default";
        if (dest.StartsWith("127.")) return "Loopback";
        if (dest.StartsWith("224.") || dest.StartsWith("255.255.255.255")) return "Broadcast/Multicast";
        if (dest.StartsWith("169.254.")) return "Link-Local";
        if (gateway == "0.0.0.0" || gateway == dest) return "Local";
        return "Remote";
    }

    private static int RouteTypePriority(string type) => type switch
    {
        "Default" => 0,
        "Remote" => 1,
        "Local" => 2,
        "Link-Local" => 3,
        "Loopback" => 4,
        "Broadcast/Multicast" => 5,
        _ => 6
    };

    private static Dictionary<string, (string Ip, string Name)> BuildInterfaceNameMap()
    {
        var map = new Dictionary<string, (string Ip, string Name)>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT InterfaceIndex, IPAddress, Description FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");

        foreach (ManagementObject obj in searcher.Get())
        {
            var index = obj["InterfaceIndex"]?.ToString() ?? "";
            var ips = obj["IPAddress"] as string[];
            var desc = obj["Description"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(index) && ips is { Length: > 0 })
                map[index] = (ips[0], desc);
        }

        using var adapterSearcher = new ManagementObjectSearcher(
            "SELECT DeviceID, NetConnectionID FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL");
        foreach (ManagementObject obj in adapterSearcher.Get())
        {
            var devId = obj["DeviceID"]?.ToString() ?? "";
            var connId = obj["NetConnectionID"]?.ToString();
            if (!string.IsNullOrEmpty(connId) && map.ContainsKey(devId))
                map[devId] = (map[devId].Ip, connId);
        }

        return map;
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
