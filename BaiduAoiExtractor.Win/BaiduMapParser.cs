using System.Globalization;
using System.Text.Json;

namespace BaiduAoiExtractor.Win;

internal static class BaiduMapParser
{
    public static string? ExtractGeoFromDetail(JsonElement data)
    {
        if (!TryGetProperty(data, "content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            using var enumerator = content.EnumerateArray();
            if (!enumerator.MoveNext())
            {
                return null;
            }

            content = enumerator.Current;
        }

        return TryGetNestedString(content, out var geo, "ext", "detail_info", "guoke_geo", "geo")
            ? geo
            : null;
    }

    public static string? ExtractUidFromSearch(JsonElement data)
    {
        return ExtractUidsFromSearch(data).FirstOrDefault();
    }

    public static IReadOnlyList<string> ExtractUidsFromSearch(JsonElement data)
    {
        if (!TryGetProperty(data, "content", out var content))
        {
            return [];
        }

        var uids = new List<string>();
        AddUids(content, uids);
        return uids
            .Where(uid => !string.IsNullOrWhiteSpace(uid))
            .Distinct()
            .Take(20)
            .ToList();
    }

    public static IReadOnlyList<PlaceSuggestion> ExtractSuggestionsFromSearch(JsonElement data)
    {
        if (!TryGetProperty(data, "content", out var content))
        {
            return [];
        }

        var suggestions = new List<PlaceSuggestion>();
        AddSuggestions(content, suggestions);

        return suggestions
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .DistinctBy(s => $"{s.Name}|{s.Address}|{s.Uid}")
            .Take(20)
            .ToList();
    }

    public static IReadOnlyList<AoiPoint> ParseGeoToBd09McPoints(string geo)
    {
        var parts = geo.Split('|');
        if (parts.Length < 3)
        {
            throw new FormatException("geo 格式异常，缺少坐标段。");
        }

        var coords = parts[2];
        if (coords.StartsWith('a'))
        {
            coords = coords[1..];
        }

        var tokens = coords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var points = new List<AoiPoint>(tokens.Length / 2);

        for (var i = 0; i < tokens.Length - 1; i += 2)
        {
            if (double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                points.Add(new AoiPoint(x, y));
            }
        }

        return points;
    }

    private static bool TryGetNestedString(JsonElement element, out string? value, params string[] path)
    {
        value = null;
        var current = element;

        foreach (var segment in path)
        {
            if (!TryGetProperty(current, segment, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = current.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }

    private static void AddSuggestion(JsonElement item, List<PlaceSuggestion> suggestions)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = GetFirstString(item, "name", "std_tag", "di_tag");
        var uid = GetFirstString(item, "uid");
        var address = GetFirstString(item, "addr", "address", "area_name");

        if (!string.IsNullOrWhiteSpace(name))
        {
            suggestions.Add(new PlaceSuggestion(name, address, uid));
        }
    }

    private static void AddSuggestions(JsonElement item, List<PlaceSuggestion> suggestions)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            AddSuggestion(item, suggestions);
            return;
        }

        if (item.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in item.EnumerateArray())
        {
            AddSuggestions(child, suggestions);
        }
    }

    private static void AddUids(JsonElement item, List<string> uids)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var uid = GetFirstString(item, "uid");
            if (!string.IsNullOrWhiteSpace(uid))
            {
                uids.Add(uid);
            }

            return;
        }

        if (item.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in item.EnumerateArray())
        {
            AddUids(child, uids);
        }
    }

    private static string? GetFirstString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }
}
