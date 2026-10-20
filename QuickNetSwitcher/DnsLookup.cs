#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace QuickNetSwitcher;

// A DNS query that goes to the network every time. System.Net.Dns answers this
// computer's own name from local information, which would make every registration
// look successful; DnsQuery with WIRE_ONLY asks the configured DNS servers instead.
public static class DnsLookup
{
    private const ushort TypeA = 1;
    private const ushort TypePtr = 12;

    private const uint BypassCache = 0x8;
    private const uint NoHostsFile = 0x40;
    private const uint WireOnly = 0x100;
    private const uint Options = BypassCache | NoHostsFile | WireOnly;

    private const int FreeRecordList = 1;

    // The fixed part of DNS_RECORD: pNext, pName, then wType, wDataLength, Flags,
    // dwTtl, dwReserved. The record's data follows it.
    private static readonly int TypeOffset = 2 * IntPtr.Size;
    private static readonly int DataOffset = 2 * IntPtr.Size + 16;

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DnsQuery(string name, ushort type, uint options,
        IntPtr extra, out IntPtr results, IntPtr reserved);

    [DllImport("dnsapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void DnsRecordListFree(IntPtr records, int freeType);

    public static List<IPAddress> QueryA(string name)
    {
        var found = new List<IPAddress>();
        Query(name, TypeA, record =>
            found.Add(new IPAddress((uint)Marshal.ReadInt32(record, DataOffset))));
        return found;
    }

    public static List<string> QueryPtr(IPAddress address)
    {
        var found = new List<string>();
        var b = address.GetAddressBytes();
        string reverse;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            reverse = $"{b[3]}.{b[2]}.{b[1]}.{b[0]}.in-addr.arpa";
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // One label per nibble, lowest first.
            var nibbles = new List<string>(64);
            for (var i = b.Length - 1; i >= 0; i--)
            {
                nibbles.Add((b[i] & 0xF).ToString("x"));
                nibbles.Add((b[i] >> 4).ToString("x"));
            }
            reverse = string.Join(".", nibbles) + ".ip6.arpa";
        }
        else
        {
            return found;
        }
        Query(reverse, TypePtr, record =>
        {
            var host = Marshal.PtrToStringUni(Marshal.ReadIntPtr(record, DataOffset));
            if (!string.IsNullOrEmpty(host))
                found.Add(host.TrimEnd('.'));
        });
        return found;
    }

    // A name that does not exist, or has no record of this type, is an empty answer
    // rather than an error: that is the result the caller is checking for.
    private static void Query(string name, ushort type, Action<IntPtr> onRecord)
    {
        var status = DnsQuery(name, type, Options, IntPtr.Zero, out var results, IntPtr.Zero);
        if (status != 0 || results == IntPtr.Zero)
            return;

        try
        {
            for (var record = results; record != IntPtr.Zero; record = Marshal.ReadIntPtr(record))
            {
                if ((ushort)Marshal.ReadInt16(record, TypeOffset) == type)
                    onRecord(record);
            }
        }
        finally
        {
            DnsRecordListFree(results, FreeRecordList);
        }
    }
}
