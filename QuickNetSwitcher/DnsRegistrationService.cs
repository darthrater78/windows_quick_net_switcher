#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;

namespace QuickNetSwitcher;

// "Register now": asks Windows to re-register in DNS, then checks the result, since
// Windows itself only says the registration has started.
//
// Runs on a background thread and stops as soon as the token is cancelled, which the
// window does when it closes -- so a registration still being checked never holds the
// app open.
public static class DnsRegistrationService
{
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private const int Attempts = 10;
    private const int MaxEventLines = 40;

    // True when nothing in the run was reported as an error.
    public static bool RegisterAndVerify(
        string adapterName, string ipAddress, string connectionSuffix,
        IProgress<LogLine> log, CancellationToken cancel)
    {
        var tracked = new TrackingProgress(log);
        Run(adapterName, ipAddress, connectionSuffix, tracked, cancel);
        return !tracked.HadError;
    }

    private static void Run(
        string adapterName, string ipAddress, string connectionSuffix,
        IProgress<LogLine> log, CancellationToken cancel)
    {
        var started = DateTime.UtcNow;

        log.Report(LogLine.Info("DNS · ipconfig /registerdns (registers every adapter with the box ticked)"));
        var result = ProcessRunner.Run(SystemPaths.Ipconfig, HelperTimeout, "/registerdns");
        if (result == null)
        {
            log.Report(LogLine.Error("DNS · ipconfig did not start or did not finish"));
            return;
        }
        foreach (var line in NonEmptyLines(result.Output))
            log.Report(LogLine.Info($"DNS ·   {line}"));
        if (result.ExitCode != 0)
        {
            log.Report(LogLine.Error($"DNS · ipconfig failed (exit code {result.ExitCode})"));
            return;
        }

        var fqdn = RegisteredName(connectionSuffix);
        if (fqdn == null)
        {
            log.Report(LogLine.Info("DNS · this computer has no DNS suffix, so there is no name to check"));
            ReportDnsClientEvents(started, log);
            return;
        }

        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            log.Report(LogLine.Error($"DNS · {adapterName} has no IPv4 address to check"));
            return;
        }

        var aMatched = WaitForARecord(fqdn, address, adapterName, log, cancel);
        if (cancel.IsCancellationRequested) return;

        var ptr = DnsLookup.QueryPtr(address);
        if (ptr.Any(n => n.Equals(fqdn, StringComparison.OrdinalIgnoreCase)))
            log.Report(LogLine.Info($"DNS · PTR  {address} → {fqdn}  ✓"));
        else if (ptr.Count > 0)
            log.Report(LogLine.Info($"DNS · PTR  {address} → {string.Join(", ", ptr)} (not this name)"));
        else
            log.Report(LogLine.Info($"DNS · PTR  no record for {address} (reverse zones are often managed by the DNS or DHCP server)"));

        ReportDnsClientEvents(started, log);

        log.Report(aMatched
            ? LogLine.Info($"DNS · finished: {fqdn} points at {adapterName}")
            : LogLine.Error($"DNS · finished: {fqdn} does not point at {address} yet"));
    }

    private static bool WaitForARecord(string fqdn, IPAddress address, string adapterName,
        IProgress<LogLine> log, CancellationToken cancel)
    {
        log.Report(LogLine.Info($"DNS · asking the DNS servers for {fqdn} (cache bypassed)"));
        string? lastSeen = null;

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var records = DnsLookup.QueryA(fqdn);
            if (records.Contains(address))
            {
                log.Report(LogLine.Info($"DNS · A    {fqdn} → {address}  matches {adapterName} ✓"));
                return true;
            }

            var seen = records.Count > 0 ? string.Join(", ", records) : "no record";
            if (seen != lastSeen)
            {
                log.Report(LogLine.Info($"DNS · A    {fqdn} → {seen}; checking again for up to 30 s"));
                lastSeen = seen;
            }

            // Wakes early when the window closes.
            if (cancel.WaitHandle.WaitOne(RetryDelay))
                return false;
        }

        log.Report(LogLine.Error($"DNS · A    no record for {fqdn} pointing at {address} after 30 s"));
        return false;
    }

    // Windows registers HOST.<primary suffix>; with no primary suffix, the connection's
    // own suffix is the name it uses.
    private static string? RegisteredName(string connectionSuffix)
    {
        var props = IPGlobalProperties.GetIPGlobalProperties();
        var suffix = !string.IsNullOrWhiteSpace(props.DomainName) ? props.DomainName : connectionSuffix;
        if (string.IsNullOrWhiteSpace(props.HostName) || string.IsNullOrWhiteSpace(suffix))
            return null;
        return $"{props.HostName}.{suffix.Trim('.')}";
    }

    // Registration failures are only ever reported in the System log, by the DNS
    // Client. Warnings and errors since the start of this run are shown as they are.
    private static void ReportDnsClientEvents(DateTime sinceUtc, IProgress<LogLine> log)
    {
        var since = sinceUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var query = "*[System[Provider[@Name='Microsoft-Windows-DNS-Client'] and (Level=2 or Level=3)" +
                    $" and TimeCreated[@SystemTime>='{since}']]]";

        var result = ProcessRunner.Run(SystemPaths.Wevtutil, HelperTimeout,
            "qe", "System", $"/q:{query}", "/f:text", "/rd:true", "/c:5");
        if (result == null || result.ExitCode != 0)
        {
            log.Report(LogLine.Error("DNS · couldn't read the System event log"));
            return;
        }

        var lines = NonEmptyLines(result.Output).Take(MaxEventLines).ToList();
        var sinceLocal = sinceUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (lines.Count == 0)
        {
            log.Report(LogLine.Info($"DNS · DNS Client warnings since {sinceLocal}: none (Windows can report failures up to 15 minutes later)"));
            return;
        }

        log.Report(LogLine.Error($"DNS · DNS Client warnings since {sinceLocal}:"));
        foreach (var line in lines)
            log.Report(LogLine.Error($"DNS ·   {line}"));
    }

    private sealed class TrackingProgress(IProgress<LogLine> inner) : IProgress<LogLine>
    {
        public bool HadError { get; private set; }

        public void Report(LogLine value)
        {
            HadError |= value.IsError;
            inner.Report(value);
        }
    }

    private static string[] NonEmptyLines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
}
