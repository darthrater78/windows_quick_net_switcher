#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace QuickNetSwitcher;

// Reverse-DNS names for the addresses in the firewall log, looked up one at a time in
// the background and cached.
//
// Addresses on the local network are always looked up. An outside address is looked up
// only when "Resolve names" is ticked: its reverse zone usually belongs to whoever
// controls the address, and a lookup tells them their traffic was noticed.
public sealed class NameResolver
{
    private const int MaxCached = 4096;

    private readonly Dictionary<string, string> _cache = new();
    private readonly HashSet<string> _pending = new();
    private readonly Queue<string> _queue = new();
    private readonly Action<string, string> _resolved;
    private readonly CancellationToken _cancel;
    private bool _running;

    public bool ResolveOutside { get; set; }

    // `resolved` is called on the UI thread with (address, name).
    public NameResolver(Action<string, string> resolved, CancellationToken cancel)
    {
        _resolved = resolved;
        _cancel = cancel;
    }

    // Called on the UI thread. Returns the name if already known.
    public string Lookup(string address)
    {
        if (_cache.TryGetValue(address, out var name)) return name;
        if (!IPAddress.TryParse(address, out var ip)) return "";

        var fixedName = FixedName(ip);
        if (fixedName != null)
        {
            _cache[address] = fixedName;
            return fixedName;
        }

        if ((IsLocal(ip) || ResolveOutside) && _pending.Add(address))
        {
            _queue.Enqueue(address);
            _ = DrainAsync();
        }
        return "";
    }

    private async Task DrainAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            while (_queue.Count > 0 && !_cancel.IsCancellationRequested)
            {
                var address = _queue.Dequeue();
                var name = await Task.Run(() => Resolve(address), _cancel);
                if (_cache.Count >= MaxCached) _cache.Clear();
                _cache[address] = name;
                _pending.Remove(address);
                _resolved(address, name);
            }
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        finally
        {
            _running = false;
        }
    }

    private static string Resolve(string address)
    {
        if (!IPAddress.TryParse(address, out var ip)) return "";
        var names = DnsLookup.QueryPtr(ip);
        return names.Count > 0 ? names[0] : "no name";
    }

    private static string? FixedName(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return "this computer";
        if (ip.Equals(IPAddress.Broadcast)) return "broadcast";
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork && b[0] >= 224 && b[0] <= 239) return "multicast";
        if (ip.IsIPv6Multicast) return "multicast";
        return null;
    }

    // RFC 1918, link-local, and IPv6 unique-local / link-local.
    public static bool IsLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC;
    }
}
