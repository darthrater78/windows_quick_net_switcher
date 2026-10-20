#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;

namespace QuickNetSwitcher;

public record InterfaceAddress(string Alias, IPAddress Address, int PrefixLength, string Profile);

public record HitLocation(string Interface, string Profile, bool BySubnet);

// Which adapter owns which address, and which firewall profile each connected network
// is under. The firewall log records neither, so a hit is placed by its local address:
// an exact match names the adapter; broadcast and multicast traffic is placed by the
// subnet of the other end instead.
//
// A snapshot: built off the UI thread, replaced whole when the network changes.
public sealed class NetworkMap
{
    public static readonly NetworkMap Empty = new(new List<InterfaceAddress>(), new Dictionary<string, List<string>>());

    private readonly List<InterfaceAddress> _addresses;

    // Profile name -> the networks using it, for "In use on Ethernet".
    public IReadOnlyDictionary<string, List<string>> InUse { get; }

    private NetworkMap(List<InterfaceAddress> addresses, Dictionary<string, List<string>> inUse)
    {
        _addresses = addresses;
        InUse = inUse;
    }

    public static NetworkMap Build()
    {
        var profiles = ReadProfiles();
        var names = new Dictionary<uint, string>();
        using (var searcher = new ManagementObjectSearcher(
            "SELECT InterfaceIndex, NetConnectionID FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
        {
            foreach (ManagementObject o in searcher.Get())
            {
                if (o["InterfaceIndex"] is { } i && o["NetConnectionID"] is string n)
                    names[Convert.ToUInt32(i)] = n;
            }
        }

        var addresses = new List<InterfaceAddress>();
        using (var searcher = new ManagementObjectSearcher(
            "SELECT InterfaceIndex, IPAddress, IPSubnet FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True"))
        {
            foreach (ManagementObject o in searcher.Get())
            {
                if (o["InterfaceIndex"] is not { } idx) continue;
                var index = Convert.ToUInt32(idx);
                var ips = o["IPAddress"] as string[] ?? Array.Empty<string>();
                var subnets = o["IPSubnet"] as string[] ?? Array.Empty<string>();
                var alias = names.TryGetValue(index, out var n) ? n : $"Interface {index}";
                var profile = profiles.TryGetValue(index, out var p) ? p : "";

                for (var k = 0; k < ips.Length; k++)
                {
                    if (!IPAddress.TryParse(ips[k], out var ip)) continue;
                    var prefix = k < subnets.Length ? PrefixLength(subnets[k]) : (ip.GetAddressBytes().Length * 8);
                    addresses.Add(new InterfaceAddress(alias, ip, prefix, profile));
                }
            }
        }

        var inUse = addresses
            .Where(a => a.Profile.Length > 0)
            .GroupBy(a => a.Profile)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Alias).Distinct().OrderBy(x => x).ToList());

        return new NetworkMap(addresses, inUse);
    }

    // MSFT_NetConnectionProfile: one per connected network, with the category Windows
    // applies the firewall profile from.
    private static Dictionary<uint, string> ReadProfiles()
    {
        var result = new Dictionary<uint, string>();
        try
        {
            var scope = new ManagementScope(@"root\StandardCimv2");
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT InterfaceIndex, NetworkCategory FROM MSFT_NetConnectionProfile"));
            foreach (ManagementObject o in searcher.Get())
            {
                if (o["InterfaceIndex"] is not { } i || o["NetworkCategory"] is not { } c) continue;
                result[Convert.ToUInt32(i)] = Convert.ToUInt32(c) switch
                {
                    0 => "Public",
                    1 => "Private",
                    2 => "Domain",
                    _ => ""
                };
            }
        }
        catch (ManagementException)
        {
            // No profiles to report; hits are then shown without one.
        }
        return result;
    }

    public HitLocation? Locate(FirewallLogEntry entry)
    {
        if (!IPAddress.TryParse(entry.SourceIp, out var src) || !IPAddress.TryParse(entry.DestIp, out var dst))
            return null;

        // The local end: the destination of what came in, the source of what went out.
        var (local, remote) = entry.Direction == "out" ? (src, dst) : (dst, src);

        var exact = _addresses.FirstOrDefault(a => a.Address.Equals(local))
                    ?? _addresses.FirstOrDefault(a => a.Address.Equals(remote));
        if (exact != null)
            return new HitLocation(exact.Alias, exact.Profile, false);

        var subnet = _addresses.FirstOrDefault(a => InSubnet(remote, a))
                     ?? _addresses.FirstOrDefault(a => InSubnet(local, a));
        return subnet == null ? null : new HitLocation(subnet.Alias, subnet.Profile, true);
    }

    private static bool InSubnet(IPAddress ip, InterfaceAddress a)
    {
        if (ip.AddressFamily != a.Address.AddressFamily) return false;
        var x = ip.GetAddressBytes();
        var y = a.Address.GetAddressBytes();
        var bits = a.PrefixLength;
        for (var i = 0; i < x.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((x[i] & mask) != (y[i] & mask)) return false;
        }
        return true;
    }

    // IPv4 subnets come as masks ("255.255.255.0"), IPv6 ones as lengths ("64").
    private static int PrefixLength(string subnet)
    {
        if (int.TryParse(subnet, out var length)) return length;
        if (!IPAddress.TryParse(subnet, out var mask)) return 32;
        return mask.GetAddressBytes().Sum(b => System.Numerics.BitOperations.PopCount(b));
    }
}
