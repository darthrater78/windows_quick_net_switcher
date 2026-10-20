#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace QuickNetSwitcher;

// One line of pfirewall.log. The interface, profile and names are filled in after
// parsing (FirewallHits, NameResolver), so they notify.
public sealed class FirewallLogEntry : INotifyPropertyChanged
{
    public DateTime When { get; init; }
    public string Time => When.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string Action { get; init; } = "";
    public string Protocol { get; init; } = "";
    public string SourceIp { get; init; } = "";
    public string SourcePort { get; init; } = "";
    public string DestIp { get; init; } = "";
    public string DestPort { get; init; } = "";
    public string Direction { get; init; } = "";

    public string Source => Endpoint(SourceIp, SourcePort);
    public string Destination => Endpoint(DestIp, DestPort);
    public bool IsDropped => Action == "Dropped";
    public bool IsAllowed => Action == "Allowed";

    private string _interface = "";
    private string _profile = "";
    private string _sourceName = "";
    private string _destName = "";

    public string Interface { get => _interface; set => Set(ref _interface, value, nameof(Interface)); }
    public string Profile { get => _profile; set => Set(ref _profile, value, nameof(Profile)); }
    public string SourceName { get => _sourceName; set => Set(ref _sourceName, value, nameof(SourceName)); }
    public string DestName { get => _destName; set => Set(ref _destName, value, nameof(DestName)); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, string name)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string Endpoint(string address, string port)
    {
        if (port == "-" || port.Length == 0) return address;
        return address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
    }
}

public record TailResult(List<FirewallLogEntry> NewEntries, long FileSize, bool Missing, bool Restarted);

// Reads pfirewall.log a piece at a time, picking up only what Windows has added since
// the last read.
//
// The file is opened for each read and closed straight after, read-only and shared for
// writing and deletion, so the firewall service can always write to it and rename it
// when it fills up -- and nothing is held open when the app closes.
public sealed class FirewallLogTail
{
    private const int StartBytes = 256 * 1024;
    private const int MaxReadBytes = 1024 * 1024;
    private const int MaxFieldLength = 64;

    private readonly string _path;
    private long _position = -1;
    private DateTime _created;
    private string _partial = "";

    public FirewallLogTail(string path) => _path = path;

    public string FileName => Path.GetFileName(_path);

    public TailResult ReadNew()
    {
        var entries = new List<FirewallLogEntry>();
        var info = new FileInfo(_path);
        if (!info.Exists)
            return new TailResult(entries, 0, Missing: true, Restarted: false);

        // A symbolic link here, or on any folder above it, could send this elevated
        // read to a network share and this account's credentials with it. Checked on
        // every read, since the file is reopened each time.
        if (HasLinkOnPath(info))
            throw new UnauthorizedAccessException("the log path goes through a link, so it isn't opened");

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        var length = stream.Length;

        // A full log is renamed to .old and a new one started. A new creation time, or a
        // file shorter than where the last read stopped, means that happened.
        var restarted = _position >= 0 && (info.CreationTimeUtc != _created || length < _position);
        var skipFirstLine = false;
        if (_position < 0 || restarted || length - _position > MaxReadBytes)
        {
            _position = Math.Max(0, length - StartBytes);
            _partial = "";
            skipFirstLine = _position > 0;
        }
        _created = info.CreationTimeUtc;

        var count = (int)(length - _position);
        if (count <= 0)
            return new TailResult(entries, length, false, restarted);

        var buffer = new byte[count];
        stream.Seek(_position, SeekOrigin.Begin);
        var read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        _position += read;

        var lines = (_partial + Encoding.UTF8.GetString(buffer, 0, read)).Split('\n');
        // The last piece has no newline yet; it is finished on the next read.
        _partial = lines[^1];

        for (var i = skipFirstLine ? 1 : 0; i < lines.Length - 1; i++)
        {
            var entry = Parse(lines[i]);
            if (entry != null)
                entries.Add(entry);
        }

        return new TailResult(entries, length, false, restarted);
    }

    private static bool HasLinkOnPath(FileInfo file)
    {
        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return true;
        for (var dir = file.Directory; dir != null; dir = dir.Parent)
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return true;
        }
        return false;
    }

    // date time action protocol src-ip dst-ip src-port dst-port size tcpflags tcpsyn
    // tcpack tcpwin icmptype icmpcode info path [pid]
    private static FirewallLogEntry? Parse(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] == '#')
            return null;

        var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 8)
            return null;

        if (!DateTime.TryParseExact($"{f[0]} {f[1]}", "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var when))
            return null;

        var action = f[2] switch
        {
            "DROP" => "Dropped",
            "ALLOW" => "Allowed",
            _ => Clip(f[2])
        };
        var direction = f.Length > 16
            ? f[16] switch { "RECEIVE" => "in", "SEND" => "out", "FORWARD" => "fwd", _ => Clip(f[16]) }
            : "";

        return new FirewallLogEntry
        {
            When = when,
            Action = action,
            Protocol = Clip(f[3]),
            SourceIp = Clip(f[4]),
            DestIp = Clip(f[5]),
            SourcePort = Clip(f[6]),
            DestPort = Clip(f[7]),
            Direction = direction
        };
    }

    private static string Clip(string value) =>
        value.Length > MaxFieldLength ? value[..MaxFieldLength] : value;
}
