#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuickNetSwitcher;

// One adapter's IPv4 settings: where the address comes from, and where DNS comes from.
// Windows allows DNS servers set by hand on a DHCP address, but not DNS from DHCP on an
// address set by hand, so DnsAuto is only ever true together with Dhcp.
public record IpConfig(
    bool Dhcp,
    string Address,
    int Prefix,
    string Gateway,
    bool DnsAuto,
    IReadOnlyList<string> Dns)
{
    public static readonly IpConfig Automatic = new(true, "", 0, "", true, Array.Empty<string>());

    public string Summary =>
        Dhcp
            ? (DnsAuto ? "address and DNS from the network" : $"DHCP · dns {string.Join(", ", Dns)}")
            : $"{Address}/{Prefix} · " + (Gateway.Length > 0 ? $"gw {Gateway}" : "no gateway");

    // Equal in what Windows would end up with: the fields that do not apply in a mode
    // are left out of the comparison.
    public bool SameAs(IpConfig other) =>
        Dhcp == other.Dhcp
        && DnsAuto == other.DnsAuto
        && (Dhcp || (Address == other.Address && Prefix == other.Prefix && Gateway == other.Gateway))
        && (DnsAuto || Dns.SequenceEqual(other.Dns));
}

// A named IpConfig, kept in settings.json. Plain properties so System.Text.Json can
// round-trip it. The file is the user's to edit, so nothing read from it is trusted:
// SettingsService drops any preset that does not pass Validate.
public class IpPreset
{
    public string Name { get; set; } = "";
    public bool Dhcp { get; set; }
    public string Address { get; set; } = "";
    public int Prefix { get; set; }
    public string Gateway { get; set; } = "";
    public bool DnsAuto { get; set; }
    public List<string> Dns { get; set; } = new();

    public IpConfig ToConfig() => new(Dhcp, Address ?? "", Prefix, Gateway ?? "", DnsAuto,
        (Dns ?? new()).ToList());

    public static IpPreset From(string name, IpConfig c) => new()
    {
        Name = name,
        Dhcp = c.Dhcp,
        Address = c.Dhcp ? "" : c.Address,
        Prefix = c.Dhcp ? 0 : c.Prefix,
        Gateway = c.Dhcp ? "" : c.Gateway,
        DnsAuto = c.DnsAuto,
        Dns = c.DnsAuto ? new() : c.Dns.ToList()
    };
}

public static class IpSettingsService
{
    public const int MaxPresets = 50;
    public const int MaxNameLength = 40;

    // Setting an address waits on duplicate address detection, which can take a few
    // seconds; the 5 s every other netsh call gets is not enough.
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(20);

    // Dotted quad only, no leading zeros. IPAddress.TryParse alone would accept "10",
    // "10.1" and "010.1.1.1", each of which means something other than it looks like.
    private static readonly Regex DottedQuad = new(
        @"^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$",
        RegexOptions.CultureInvariant);

    public static bool TryParseIpv4(string text, out uint value)
    {
        value = 0;
        if (!DottedQuad.IsMatch(text)) return false;
        foreach (var part in text.Split('.'))
            value = (value << 8) | uint.Parse(part);
        return true;
    }

    public static uint PrefixToMask(int prefix) =>
        prefix <= 0 ? 0u : prefix >= 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);

    public static string ToDotted(uint v) =>
        $"{v >> 24}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";

    public static string? ValidateName(string name, IEnumerable<IpPreset> existing)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Give the preset a name.";
        if (name.Length > MaxNameLength) return $"Keep the name to {MaxNameLength} characters.";
        if (name.Any(char.IsControl)) return "The name can't contain control characters.";
        var count = existing.Count(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (count >= MaxPresets) return $"There are already {MaxPresets} presets.";
        return null;
    }

    // Null when the settings are usable; otherwise what is wrong, in words for the
    // dialog.
    public static string? Validate(IpConfig c)
    {
        if (c.DnsAuto && !c.Dhcp)
            return "DNS can only come from the network when the address does.";

        if (!c.Dhcp)
        {
            if (!TryParseIpv4(c.Address, out var ip))
                return "Enter the IP address as four numbers, like 192.168.1.20.";
            if (c.Prefix < 1 || c.Prefix > 32)
                return "The subnet prefix is a number from 1 to 32.";

            var first = ip >> 24;
            if (first == 0 || first == 127 || first >= 224)
                return "That address can't be given to an adapter.";

            var mask = PrefixToMask(c.Prefix);
            if (c.Prefix <= 30 && ((ip & ~mask) == 0 || (ip & ~mask) == ~mask))
                return $"{c.Address} is the network or broadcast address of /{c.Prefix}.";

            // The app treats exactly this as a ZTNA tunnel and locks it, so setting it
            // here would leave the adapter with no way back from inside the app.
            if (c.Prefix == 32 && c.Gateway.Length == 0)
                return "A /32 address with no gateway is how ZTNA adapters look, and the app would lock it. Use a shorter prefix or add a gateway.";

            if (c.Gateway.Length > 0)
            {
                if (!TryParseIpv4(c.Gateway, out var gw))
                    return "Enter the gateway as four numbers, or leave it empty.";
                if (gw == ip)
                    return "The gateway can't be this adapter's own address.";
                if ((gw & mask) != (ip & mask))
                    return $"The gateway is outside {ToDotted(ip & mask)}/{c.Prefix}.";
            }
        }

        if (!c.DnsAuto)
        {
            if (c.Dns.Count > 2) return "Enter at most two DNS servers.";
            foreach (var d in c.Dns)
                if (!TryParseIpv4(d, out _))
                    return $"DNS server \"{d}\" isn't an IPv4 address.";
        }

        return null;
    }

    // Moves an adapter from `from` to `to` through netsh: the address, then DNS, each
    // only when it actually changes. netsh fails a "set" to what is already there
    // ("DHCP is already enabled on this interface", "The object already exists"), so a
    // DNS-only edit must not touch the address. Every value has passed Validate and
    // goes to netsh as its own argument (ProcessRunner), so an adapter name or address
    // cannot add a token of its own. register= is passed explicitly because netsh
    // otherwise resets "Register in DNS" to its default.
    //
    // Null on success; otherwise netsh's own words for what went wrong.
    public static string? Apply(string interfaceAlias, IpConfig from, IpConfig to,
        bool registerInDns, bool registerSuffix)
    {
        if (string.IsNullOrEmpty(interfaceAlias))
            return "No adapter name.";
        if (Validate(to) is { } problem)
            return problem;

        var name = $"name={interfaceAlias}";
        var register = !registerInDns ? "register=none" : registerSuffix ? "register=both" : "register=primary";

        var addressChanges = to.Dhcp != from.Dhcp
            || (!to.Dhcp && (to.Address != from.Address || to.Prefix != from.Prefix || to.Gateway != from.Gateway));
        var dnsChanges = to.DnsAuto != from.DnsAuto
            || (!to.DnsAuto && !to.Dns.SequenceEqual(from.Dns));

        if (addressChanges)
        {
            var error = to.Dhcp
                ? Netsh("interface", "ipv4", "set", "address", name, "source=dhcp")
                : Netsh("interface", "ipv4", "set", "address", name, "source=static",
                    $"address={to.Address}", $"mask={ToDotted(PrefixToMask(to.Prefix))}",
                    to.Gateway.Length > 0 ? $"gateway={to.Gateway}" : "gateway=none");
            if (error != null) return error;
        }

        if (!dnsChanges)
            return null;

        if (to.DnsAuto)
            return Netsh("interface", "ipv4", "set", "dnsservers", name, "source=dhcp", register);

        if (to.Dns.Count == 0)
            return Netsh("interface", "ipv4", "set", "dnsservers", name, "source=static", "address=none", register);

        var dnsError = Netsh("interface", "ipv4", "set", "dnsservers", name, "source=static",
            $"address={to.Dns[0]}", register, "validate=no");
        for (int i = 1; dnsError == null && i < to.Dns.Count; i++)
            dnsError = Netsh("interface", "ipv4", "add", "dnsservers", name,
                $"address={to.Dns[i]}", $"index={i + 1}", "validate=no");
        return dnsError;
    }

    // Null on success; otherwise netsh's output, which is where it puts its errors.
    private static string? Netsh(params string[] args)
    {
        var result = ProcessRunner.Run(SystemPaths.Netsh, ApplyTimeout, args);
        if (result == null) return "netsh did not finish in time.";
        if (result.ExitCode == 0) return null;
        var text = string.Join(" ", result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0));
        return text.Length > 0 ? $"netsh: {text}" : $"netsh exited with code {result.ExitCode}.";
    }
}
