namespace BaiduAoiExtractor.Win;

internal sealed record AoiPoint(double X, double Y);

internal sealed record CrawlSettings(
    bool Headless,
    bool Debug,
    int MinDelayMs,
    int MaxDelayMs);

internal sealed record PlaceInput(string Query, string? Uid = null, string? DisplayName = null, string? Address = null)
{
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Query : DisplayName;
}

internal sealed record PlaceSuggestion(string Name, string? Address, string? Uid)
{
    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Address) ? Name : $"{Name}  |  {Address}";
    }
}

internal sealed record CrawlResult(
    string PlaceName,
    string? Uid,
    string? Geo,
    IReadOnlyList<AoiPoint> Points,
    bool Success,
    string Message)
{
    public static CrawlResult Fail(string placeName, string? uid, string message)
    {
        return new CrawlResult(placeName, uid, null, Array.Empty<AoiPoint>(), false, message);
    }

    public static CrawlResult Ok(string placeName, string? uid, string geo, IReadOnlyList<AoiPoint> points)
    {
        return new CrawlResult(placeName, uid, geo, points, true, $"成功导出 {points.Count} 个坐标点");
    }
}
