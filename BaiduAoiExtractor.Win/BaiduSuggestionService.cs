using System.Text.Json;
using Microsoft.Playwright;

namespace BaiduAoiExtractor.Win;

internal sealed class BaiduSuggestionService
{
    public async Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var suggestions = new List<PlaceSuggestion>();
        var needRecaptcha = false;

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
            Locale = "zh-CN"
        });

        var page = await context.NewPageAsync();
        page.Response += async (_, response) =>
        {
            try
            {
                if (!response.Url.Contains("qt=s", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var text = await response.TextAsync();
                using var document = JsonDocument.Parse(text);
                if (NeedsRecaptcha(document.RootElement))
                {
                    needRecaptcha = true;
                    return;
                }

                suggestions.AddRange(BaiduMapParser.ExtractSuggestionsFromSearch(document.RootElement));
            }
            catch
            {
                // Search suggestions are best-effort; the main extraction path can still use manual input.
            }
        };

        await page.GotoAsync("https://map.baidu.com/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 20000
        });
        await page.WaitForTimeoutAsync(2500);
        cancellationToken.ThrowIfCancellationRequested();

        var searchBox = page.Locator("#sole-input");
        await searchBox.ClickAsync(new LocatorClickOptions { Timeout = 8000 });
        await searchBox.FillAsync(query);
        await page.WaitForTimeoutAsync(300);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(4500);
        await context.CloseAsync();

        var result = suggestions
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .DistinctBy(s => $"{s.Name}|{s.Address}|{s.Uid}")
            .Take(20)
            .ToList();

        if (result.Count == 0 && needRecaptcha)
        {
            throw new InvalidOperationException("百度地图返回了风控验证，当前无法获取搜索候选。可以直接点“添加选中”使用输入内容，或从 Excel 导入地点。");
        }

        return result;
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        var options = new BrowserTypeLaunchOptions
        {
            Headless = true
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

    private static bool NeedsRecaptcha(JsonElement root)
    {
        return root.TryGetProperty("result", out var result) &&
               result.ValueKind == JsonValueKind.Object &&
               result.TryGetProperty("anti_session", out var antiSession) &&
               antiSession.ValueKind == JsonValueKind.Object &&
               antiSession.TryGetProperty("need_recaptcha", out var needRecaptcha) &&
               needRecaptcha.ValueKind == JsonValueKind.True;
    }
}
