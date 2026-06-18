using System.Text.Json;
using Microsoft.Playwright;

namespace BaiduAoiExtractor.Win;

internal sealed class BaiduAoiCrawler
{
    private readonly Action<string> _log;
    private readonly Random _random = new();

    public BaiduAoiCrawler(Action<string> log)
    {
        _log = log;
    }

    public Task<CrawlResult> CrawlAsync(string placeName, CrawlSettings settings, CancellationToken cancellationToken)
    {
        return CrawlAsync(new PlaceInput(placeName), settings, cancellationToken);
    }

    public async Task<CrawlResult> CrawlAsync(PlaceInput input, CrawlSettings settings, CancellationToken cancellationToken)
    {
        var placeName = input.Label;
        var selectedUid = input.Uid;
        var searchUids = new List<string>();
        string? geo = null;
        string? successUid = null;

        try
        {
            await RandomDelayAsync(settings, cancellationToken);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await LaunchBrowserAsync(playwright, settings.Headless);
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "zh-CN"
            });

            var page = await context.NewPageAsync();
            await page.AddInitScriptAsync("Object.defineProperty(navigator,'webdriver',{get:()=>false})");

            page.Response += async (_, response) =>
            {
                if (geo is not null)
                {
                    return;
                }

                try
                {
                    var url = response.Url;
                    if (url.Contains("detailConInfo", StringComparison.OrdinalIgnoreCase))
                    {
                        using var document = await ResponseJsonAsync(response);
                        var parsedGeo = BaiduMapParser.ExtractGeoFromDetail(document.RootElement);
                        if (!string.IsNullOrWhiteSpace(parsedGeo))
                        {
                            geo = parsedGeo;
                            _log($"[{placeName}] 页面响应里捕获到 AOI。");
                        }
                        else
                        {
                            Debug(settings, $"[{placeName}] 捕获到 detailConInfo，但里面没有 guoke_geo.geo。");
                        }
                    }

                    if (url.Contains("qt=s", StringComparison.OrdinalIgnoreCase))
                    {
                        using var document = await ResponseJsonAsync(response);
                        var uids = BaiduMapParser.ExtractUidsFromSearch(document.RootElement);
                        foreach (var uid in uids)
                        {
                            if (!searchUids.Contains(uid))
                            {
                                searchUids.Add(uid);
                            }
                        }

                        if (uids.Count > 0)
                        {
                            Debug(settings, $"[{placeName}] 页面搜索捕获 UID: {string.Join(", ", uids.Take(5))}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug(settings, $"[{placeName}] 响应解析失败: {ex.Message}");
                }
            };

            _log($"[{placeName}] 打开百度地图...");
            await page.GotoAsync("https://map.baidu.com/", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 20000
            });
            await page.WaitForTimeoutAsync(3000);
            cancellationToken.ThrowIfCancellationRequested();

            _log($"[{placeName}] 在页面内搜索，建立地图会话...");
            await SearchInPageAsync(page, input.Query);
            await page.WaitForTimeoutAsync(4500);

            var candidates = BuildUidCandidates(selectedUid, searchUids);
            if (candidates.Count == 0)
            {
                _log($"[{placeName}] 没有捕获到 UID，尝试点击搜索结果。");
                await TryClickSearchResultAsync(page, placeName, settings);
                await page.WaitForTimeoutAsync(3000);
                candidates = BuildUidCandidates(selectedUid, searchUids);
            }

            if (geo is null)
            {
                var clickedGeo = await TryClickAndCaptureDetailAsync(page, placeName, input.Query, settings, cancellationToken);
                if (!string.IsNullOrWhiteSpace(clickedGeo))
                {
                    geo = clickedGeo;
                }
            }

            foreach (var uid in candidates)
            {
                if (geo is not null)
                {
                    successUid = uid;
                    break;
                }

                _log($"[{placeName}] 尝试 UID: {uid}");
                var detailGeo = await TryDetailApiAsync(context, page, placeName, uid, settings);
                if (!string.IsNullOrWhiteSpace(detailGeo))
                {
                    geo = detailGeo;
                    successUid = uid;
                    break;
                }

                if (geo is not null)
                {
                    successUid = uid;
                    break;
                }
            }

            if (geo is null)
            {
                _log($"[{placeName}] 等待 AOI 接口返回...");
                for (var i = 0; i < 15 && geo is null; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await page.WaitForTimeoutAsync(1000);
                }
            }

            await context.CloseAsync();

            if (string.IsNullOrWhiteSpace(geo))
            {
                var tried = candidates.Count == 0 ? "无 UID" : string.Join(", ", candidates);
                return CrawlResult.Fail(placeName, selectedUid ?? searchUids.FirstOrDefault(), $"未拿到 AOI。已尝试 UID: {tried}。请确认该地点在百度地图详情里确实有 guoke_geo.geo。");
            }

            var bd09McPoints = BaiduMapParser.ParseGeoToBd09McPoints(geo);
            if (bd09McPoints.Count < 3)
            {
                return CrawlResult.Fail(placeName, successUid ?? selectedUid ?? searchUids.FirstOrDefault(), $"轮廓坐标点过少: {bd09McPoints.Count}");
            }

            var wgs84Points = CoordinateConverter.Bd09McToWgs84(bd09McPoints);
            return CrawlResult.Ok(placeName, successUid ?? selectedUid ?? searchUids.FirstOrDefault(), geo, wgs84Points);
        }
        catch (OperationCanceledException)
        {
            return CrawlResult.Fail(placeName, selectedUid ?? searchUids.FirstOrDefault(), "已取消。");
        }
        catch (Exception ex)
        {
            return CrawlResult.Fail(placeName, selectedUid ?? searchUids.FirstOrDefault(), ex.Message);
        }
    }

    private static IReadOnlyList<string> BuildUidCandidates(string? selectedUid, IReadOnlyList<string> searchUids)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectedUid))
        {
            candidates.Add(selectedUid);
        }

        foreach (var uid in searchUids)
        {
            if (!candidates.Contains(uid))
            {
                candidates.Add(uid);
            }
        }

        return candidates;
    }

    private static async Task SearchInPageAsync(IPage page, string query)
    {
        var searchBox = page.Locator("#sole-input");
        await searchBox.ClickAsync(new LocatorClickOptions { Timeout = 8000 });
        await page.WaitForTimeoutAsync(300);
        await searchBox.FillAsync(query);
        await page.WaitForTimeoutAsync(500);
        await page.Keyboard.PressAsync("Enter");
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright, bool headless)
    {
        var options = new BrowserTypeLaunchOptions
        {
            Headless = headless,
            Args =
            [
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-blink-features=AutomationControlled",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--disable-software-rasterizer",
                "--js-flags=--max-old-space-size=512"
            ]
        };

        try
        {
            options.Channel = "chrome";
            return await playwright.Chromium.LaunchAsync(options);
        }
        catch
        {
            options.Channel = null;
            return await playwright.Chromium.LaunchAsync(options);
        }
    }

    private static async Task<JsonDocument> ResponseJsonAsync(IResponse response)
    {
        var text = await response.TextAsync();
        return JsonDocument.Parse(text);
    }

    private async Task<string?> TryDetailApiAsync(IBrowserContext context, IPage page, string placeName, string uid, CrawlSettings settings)
    {
        var apiUrl = "https://map.baidu.com/?uid=" + Uri.EscapeDataString(uid) +
                     "&ugc_type=3&ugc_ver=1&qt=detailConInfo&device_ratio=1&compat=1";

        for (var retry = 0; retry < 2; retry++)
        {
            try
            {
                var response = await context.APIRequest.GetAsync(apiUrl, new APIRequestContextOptions { Timeout = 15000 });
                if (response.Ok)
                {
                    using var document = JsonDocument.Parse(await response.TextAsync());
                    var parsedGeo = BaiduMapParser.ExtractGeoFromDetail(document.RootElement);
                    if (!string.IsNullOrWhiteSpace(parsedGeo))
                    {
                        _log($"[{placeName}] 通过详情接口拿到 AOI。");
                        return parsedGeo;
                    }

                    Debug(settings, $"[{placeName}] 详情接口返回成功，但 UID {uid} 没有 guoke_geo.geo。");
                }
            }
            catch (Exception ex)
            {
                Debug(settings, $"[{placeName}] 详情接口失败 {retry + 1}: {ex.Message}");
            }

            await page.WaitForTimeoutAsync(1000);
        }

        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                var json = await page.EvaluateAsync<string>(
                    @"uid => {
                        const url = 'https://map.baidu.com/?uid=' + uid +
                            '&ugc_type=3&ugc_ver=1&qt=detailConInfo&device_ratio=1&compat=1';
                        return fetch(url).then(r => r.text()).catch(e => 'FETCH_ERROR: ' + e.message);
                    }",
                    uid);

                if (!json.StartsWith("FETCH_ERROR", StringComparison.Ordinal))
                {
                    using var document = JsonDocument.Parse(json);
                    var parsedGeo = BaiduMapParser.ExtractGeoFromDetail(document.RootElement);
                    if (!string.IsNullOrWhiteSpace(parsedGeo))
                    {
                        _log($"[{placeName}] 通过页面 fetch 拿到 AOI。");
                        return parsedGeo;
                    }

                    Debug(settings, $"[{placeName}] 页面 fetch 返回成功，但 UID {uid} 没有 guoke_geo.geo。");
                }
            }
            catch (Exception ex)
            {
                Debug(settings, $"[{placeName}] 页面 fetch 失败 {retry + 1}: {ex.Message}");
            }

            await page.WaitForTimeoutAsync(1500);
        }

        return null;
    }

    private async Task TryClickSearchResultAsync(IPage page, string placeName, CrawlSettings settings)
    {
        try
        {
            _log($"[{placeName}] 尝试点击搜索结果...");
            var prefix = placeName.Length >= 2 ? placeName[..2] : placeName;
            var link = page.Locator("a").Filter(new LocatorFilterOptions { HasTextString = prefix }).First;
            await link.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
            await link.ClickAsync();
            await page.WaitForTimeoutAsync(3000);
        }
        catch (Exception ex)
        {
            Debug(settings, $"[{placeName}] 点击搜索结果失败: {ex.Message}");
        }
    }

    private async Task<string?> TryClickAndCaptureDetailAsync(IPage page, string placeName, string query, CrawlSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            string? clickedGeo = null;
            page.Response += async (_, response) =>
            {
                try
                {
                    if (response.Url.Contains("detailConInfo", StringComparison.OrdinalIgnoreCase))
                    {
                        using var document = await ResponseJsonAsync(response);
                        clickedGeo = BaiduMapParser.ExtractGeoFromDetail(document.RootElement);
                        if (!string.IsNullOrWhiteSpace(clickedGeo))
                        {
                            _log($"[{placeName}] 点击结果后抓到 AOI。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug(settings, $"[{placeName}] 点击后详情解析失败: {ex.Message}");
                }
            };

            var target = page.Locator("a").Filter(new LocatorFilterOptions { HasTextString = query }).First;
            if (await target.CountAsync() == 0)
            {
                target = page.Locator("a").Filter(new LocatorFilterOptions { HasTextString = placeName }).First;
            }

            await target.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await target.ClickAsync();

            for (var i = 0; i < 10 && string.IsNullOrWhiteSpace(clickedGeo); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await page.WaitForTimeoutAsync(1000);
            }

            return clickedGeo;
        }
        catch (Exception ex)
        {
            Debug(settings, $"[{placeName}] 点击结果捕获详情失败: {ex.Message}");
            return null;
        }
    }

    private Task RandomDelayAsync(CrawlSettings settings, CancellationToken cancellationToken)
    {
        var min = Math.Max(0, settings.MinDelayMs);
        var max = Math.Max(min, settings.MaxDelayMs);
        var delay = max == min ? min : _random.Next(min, max + 1);
        return Task.Delay(delay, cancellationToken);
    }

    private void Debug(CrawlSettings settings, string message)
    {
        if (settings.Debug)
        {
            _log(message);
        }
    }
}
