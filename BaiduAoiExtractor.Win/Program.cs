namespace BaiduAoiExtractor.Win;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        var bundledBrowsers = Path.Combine(AppContext.BaseDirectory, ".ms-playwright");
        if (Directory.Exists(bundledBrowsers))
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", bundledBrowsers);
        }

        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 3 && args[1].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
        {
            RunSelfTestAsync(string.Join(' ', args.Skip(2))).GetAwaiter().GetResult();
            return;
        }

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }

    private static async Task RunSelfTestAsync(string placeName)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "self-test.log");
        using var writer = new StreamWriter(logPath, false, System.Text.Encoding.UTF8);
        void Log(string message)
        {
            writer.WriteLine(message);
            writer.Flush();
        }

        var crawler = new BaiduAoiCrawler(Log);
        var settings = new CrawlSettings(
            Headless: true,
            Debug: true,
            MinDelayMs: 0,
            MaxDelayMs: 0);
        var result = await crawler.CrawlAsync(placeName, settings, CancellationToken.None);
        Log($"SUCCESS={result.Success}");
        Log($"UID={result.Uid}");
        Log($"POINTS={result.Points.Count}");
        Log($"MESSAGE={result.Message}");
    }
}
