using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace QuickNetSwitcher;

public static class AdapterOrderService
{
    private static readonly string OrderFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickNetSwitcher", "adapter_order.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(OrderFile)) return new();
            var json = File.ReadAllText(OrderFile);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        // A missing, unreadable or corrupt file starts from defaults rather than
        // stopping the app.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public static bool Save(IEnumerable<string> adapterIds)
    {
        try
        {
            var dir = Path.GetDirectoryName(OrderFile)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(OrderFile, JsonSerializer.Serialize(adapterIds.ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return true;
    }

    public static List<T> ApplyOrder<T>(List<T> adapters, Func<T, string> getId)
    {
        var savedOrder = Load();
        if (savedOrder.Count == 0) return adapters;

        var lookup = adapters.ToDictionary(getId);
        var ordered = new List<T>();

        foreach (var id in savedOrder)
        {
            if (lookup.Remove(id, out var item))
                ordered.Add(item);
        }

        ordered.AddRange(lookup.Values);
        return ordered;
    }
}
