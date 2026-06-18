using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;

namespace BaiduAoiExtractor.Win;

internal static class Exporters
{
    public static void SaveAll(CrawlResult result, string outputDir, bool exportExcel, bool exportGeoJson)
    {
        Directory.CreateDirectory(outputDir);
        var safeName = MakeSafeFileName(result.PlaceName);
        var stem = Path.Combine(outputDir, $"{safeName}_轮廓");

        SaveCsv(result.Points, stem + ".csv");
        SaveShapefile(result.PlaceName, result.Points, stem + ".shp");

        if (exportGeoJson)
        {
            SaveGeoJson(result, stem + ".geojson");
        }
    }

    public static void SaveBatchExcel(IReadOnlyList<CrawlResult> results, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"AOI导出汇总_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

        using var workbook = new XLWorkbook();
        var summary = workbook.AddWorksheet("汇总");
        summary.Cell(1, 1).Value = "地点";
        summary.Cell(1, 2).Value = "UID";
        summary.Cell(1, 3).Value = "状态";
        summary.Cell(1, 4).Value = "点数";
        summary.Cell(1, 5).Value = "信息";

        for (var i = 0; i < results.Count; i++)
        {
            var row = i + 2;
            var result = results[i];
            summary.Cell(row, 1).Value = result.PlaceName;
            summary.Cell(row, 2).Value = result.Uid ?? string.Empty;
            summary.Cell(row, 3).Value = result.Success ? "成功" : "失败";
            summary.Cell(row, 4).Value = result.Points.Count;
            summary.Cell(row, 5).Value = result.Message;

            if (result.Success)
            {
                var sheetName = MakeWorksheetName(result.PlaceName, workbook);
                var sheet = workbook.AddWorksheet(sheetName);
                sheet.Cell(1, 1).Value = "X";
                sheet.Cell(1, 2).Value = "Y";

                for (var p = 0; p < result.Points.Count; p++)
                {
                    sheet.Cell(p + 2, 1).Value = result.Points[p].X;
                    sheet.Cell(p + 2, 2).Value = result.Points[p].Y;
                }

                sheet.Columns().AdjustToContents();
            }
        }

        summary.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    public static IReadOnlyList<PlaceInput> ReadPlacesFromExcel(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var rows = sheet.RangeUsed()?.RowsUsed().ToList() ?? [];
        if (rows.Count == 0)
        {
            return [];
        }

        var firstRow = rows[0];
        var headerMap = firstRow.CellsUsed()
            .ToDictionary(c => NormalizeHeader(c.GetString()), c => c.Address.ColumnNumber);

        var hasHeader = headerMap.Keys.Any(k => k is "地点" or "地址" or "名称" or "name" or "address" or "place" or "query");
        var queryColumn = FindColumn(headerMap, "地点", "地址", "名称", "name", "address", "place", "query") ?? 1;
        var uidColumn = FindColumn(headerMap, "uid");
        var startIndex = hasHeader ? 1 : 0;

        var places = new List<PlaceInput>();
        foreach (var row in rows.Skip(startIndex))
        {
            var query = row.Cell(queryColumn).GetString().Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                continue;
            }

            var uid = uidColumn is null ? null : row.Cell(uidColumn.Value).GetString().Trim();
            places.Add(new PlaceInput(query, string.IsNullOrWhiteSpace(uid) ? null : uid));
        }

        return places.DistinctBy(p => p.Query + "|" + p.Uid).ToList();
    }

    private static int? FindColumn(Dictionary<string, int> headerMap, params string[] names)
    {
        foreach (var name in names.Select(NormalizeHeader))
        {
            if (headerMap.TryGetValue(name, out var column))
            {
                return column;
            }
        }

        return null;
    }

    private static string NormalizeHeader(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static void SaveCsv(IReadOnlyList<AoiPoint> points, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("X,Y");

        foreach (var point in points)
        {
            writer.Write(point.X.ToString("F6", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(point.Y.ToString("F6", CultureInfo.InvariantCulture));
        }
    }

    private static void SaveGeoJson(CrawlResult result, string path)
    {
        var ring = CloseRing(result.Points)
            .Select(p => new[] { p.X, p.Y })
            .ToArray();

        var geoJson = new
        {
            type = "FeatureCollection",
            features = new[]
            {
                new
                {
                    type = "Feature",
                    properties = new
                    {
                        name = result.PlaceName,
                        uid = result.Uid,
                        points = result.Points.Count
                    },
                    geometry = new
                    {
                        type = "Polygon",
                        coordinates = new[] { ring }
                    }
                }
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, JsonSerializer.Serialize(geoJson, options), new UTF8Encoding(true));
    }

    private static void SaveShapefile(string placeName, IReadOnlyList<AoiPoint> points, string shpPath)
    {
        if (points.Count < 3)
        {
            throw new InvalidOperationException("坐标点少于 3 个，无法导出 Polygon Shapefile。");
        }

        var ring = CloseRing(points);
        var bbox = GetBoundingBox(ring);
        var basePath = Path.Combine(Path.GetDirectoryName(shpPath) ?? string.Empty, Path.GetFileNameWithoutExtension(shpPath));

        WriteShp(basePath + ".shp", ring, bbox);
        WriteShx(basePath + ".shx", ring.Count, bbox);
        WriteDbf(basePath + ".dbf", placeName, ring.Count);
        File.WriteAllText(basePath + ".prj", Wgs84Prj, Encoding.ASCII);
        File.WriteAllText(basePath + ".cpg", "UTF-8", Encoding.ASCII);
    }

    private static IReadOnlyList<AoiPoint> CloseRing(IReadOnlyList<AoiPoint> points)
    {
        var ring = new List<AoiPoint>(points.Count + 1);
        ring.AddRange(points);

        var first = ring[0];
        var last = ring[^1];
        if (Math.Abs(first.X - last.X) > 0.0000001 || Math.Abs(first.Y - last.Y) > 0.0000001)
        {
            ring.Add(first);
        }

        return ring;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) GetBoundingBox(IReadOnlyList<AoiPoint> points)
    {
        return (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    private static void WriteShp(string path, IReadOnlyList<AoiPoint> points, (double MinX, double MinY, double MaxX, double MaxY) bbox)
    {
        var contentBytes = 48 + points.Count * 16;
        var fileLengthWords = (100 + 8 + contentBytes) / 2;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        WriteHeader(writer, fileLengthWords, bbox);

        WriteInt32BigEndian(writer, 1);
        WriteInt32BigEndian(writer, contentBytes / 2);
        WritePolygonContent(writer, points, bbox);
    }

    private static void WriteShx(string path, int pointCount, (double MinX, double MinY, double MaxX, double MaxY) bbox)
    {
        var contentBytes = 48 + pointCount * 16;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        WriteHeader(writer, 54, bbox);
        WriteInt32BigEndian(writer, 50);
        WriteInt32BigEndian(writer, contentBytes / 2);
    }

    private static void WriteHeader(BinaryWriter writer, int fileLengthWords, (double MinX, double MinY, double MaxX, double MaxY) bbox)
    {
        WriteInt32BigEndian(writer, 9994);
        for (var i = 0; i < 5; i++)
        {
            WriteInt32BigEndian(writer, 0);
        }

        WriteInt32BigEndian(writer, fileLengthWords);
        writer.Write(1000);
        writer.Write(5);
        writer.Write(bbox.MinX);
        writer.Write(bbox.MinY);
        writer.Write(bbox.MaxX);
        writer.Write(bbox.MaxY);
        writer.Write(0.0);
        writer.Write(0.0);
        writer.Write(0.0);
        writer.Write(0.0);
    }

    private static void WritePolygonContent(BinaryWriter writer, IReadOnlyList<AoiPoint> points, (double MinX, double MinY, double MaxX, double MaxY) bbox)
    {
        writer.Write(5);
        writer.Write(bbox.MinX);
        writer.Write(bbox.MinY);
        writer.Write(bbox.MaxX);
        writer.Write(bbox.MaxY);
        writer.Write(1);
        writer.Write(points.Count);
        writer.Write(0);

        foreach (var point in points)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
        }
    }

    private static void WriteDbf(string path, string placeName, int pointCount)
    {
        var encoding = Encoding.UTF8;
        var now = DateTime.Now;
        const int fieldCount = 2;
        const int headerLength = 32 + fieldCount * 32 + 1;
        const int recordLength = 1 + 80 + 10;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, encoding);
        writer.Write((byte)0x03);
        writer.Write((byte)(now.Year - 1900));
        writer.Write((byte)now.Month);
        writer.Write((byte)now.Day);
        writer.Write(1);
        writer.Write((short)headerLength);
        writer.Write((short)recordLength);
        writer.Write(new byte[20]);

        WriteDbfField(writer, "name", 'C', 80, 0);
        WriteDbfField(writer, "points", 'N', 10, 0);
        writer.Write((byte)0x0D);

        writer.Write((byte)0x20);
        WriteDbfValue(writer, placeName, 80, alignRight: false, encoding);
        WriteDbfValue(writer, pointCount.ToString(CultureInfo.InvariantCulture), 10, alignRight: true, encoding);
        writer.Write((byte)0x1A);
    }

    private static void WriteDbfField(BinaryWriter writer, string name, char type, byte length, byte decimals)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var buffer = new byte[11];
        Array.Copy(nameBytes, buffer, Math.Min(nameBytes.Length, buffer.Length));
        writer.Write(buffer);
        writer.Write((byte)type);
        writer.Write(new byte[4]);
        writer.Write(length);
        writer.Write(decimals);
        writer.Write(new byte[14]);
    }

    private static void WriteDbfValue(BinaryWriter writer, string value, int width, bool alignRight, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value);
        if (bytes.Length > width)
        {
            bytes = bytes.Take(width).ToArray();
        }

        var buffer = Enumerable.Repeat((byte)' ', width).ToArray();
        var offset = alignRight ? width - bytes.Length : 0;
        Array.Copy(bytes, 0, buffer, Math.Max(offset, 0), bytes.Length);
        writer.Write(buffer);
    }

    private static void WriteInt32BigEndian(BinaryWriter writer, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        writer.Write(bytes);
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.Length == 0 ? "未命名地点" : builder.ToString();
    }

    private static string MakeWorksheetName(string placeName, XLWorkbook workbook)
    {
        var invalid = new HashSet<char>(['\\', '/', '*', '?', ':', '[', ']']);
        var name = new string(placeName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "坐标";
        }

        name = name.Length > 25 ? name[..25] : name;
        var candidate = name;
        var index = 1;
        while (workbook.Worksheets.Any(s => s.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{name}_{index++}";
            if (candidate.Length > 31)
            {
                candidate = candidate[..31];
            }
        }

        return candidate;
    }

    private const string Wgs84Prj =
        "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]]," +
        "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4326\"]]";
}
