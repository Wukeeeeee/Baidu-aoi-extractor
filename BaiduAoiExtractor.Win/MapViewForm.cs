using System.Text;

namespace BaiduAoiExtractor.Win;

/// <summary>
/// 地图查看窗口 — 用 Leaflet.js + OpenStreetMap 显示 AOI 建筑轮廓
/// 使用内置 WebBrowser 控件，无需额外 NuGet 包
/// </summary>
internal sealed partial class MapViewForm : Form
{
    private readonly WebBrowser _webBrowser = new()
    {
        Dock = DockStyle.Fill,
        AllowWebBrowserDrop = false,
        IsWebBrowserContextMenuEnabled = false,
        WebBrowserShortcutsEnabled = false,
        ScriptErrorsSuppressed = true,
    };

    private readonly string _placeName;
    private readonly IReadOnlyList<AoiPoint> _points;
    private readonly string _tempHtmlPath;

    public MapViewForm(string placeName, IReadOnlyList<AoiPoint> points)
    {
        _placeName = placeName;
        _points = points;
        _tempHtmlPath = Path.Combine(Path.GetTempPath(), $"aoi_map_{Guid.NewGuid():N}.html");

        InitializeComponent();
        Load += OnLoadAsync;
        FormClosed += (_, _) => CleanupTempFile();
    }

    private void InitializeComponent()
    {
        Text = $"建筑轮廓 — {_placeName}";
        Size = new Size(960, 720);
        MinimumSize = new Size(640, 480);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;

        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = Color.FromArgb(24, 28, 34),
            ForeColor = Color.FromArgb(200, 200, 200),
        };
        var statusLabel = new ToolStripStatusLabel
        {
            Text = $" {_placeName}  |  {_points.Count} 个坐标点  |  底图: © OpenStreetMap 贡献者",
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        statusStrip.Items.Add(statusLabel);

        Controls.Add(_webBrowser);
        Controls.Add(statusStrip);
    }

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        await Task.Run(() => GenerateHtml());
        _webBrowser.Navigate(_tempHtmlPath);
    }

    private void GenerateHtml()
    {
        var geoJson = BuildGeoJson();
        var centerLon = _points.Average(p => p.X);
        var centerLat = _points.Average(p => p.Y);

        var html = $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<meta http-equiv='X-UA-Compatible' content='IE=edge'/>
<title>{EscapeHtml(_placeName)}</title>
<link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'/>
<script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
<style>
  body {{ margin:0; padding:0; }}
  #map {{ width:100vw; height:100vh; }}
  .info {{ padding:6px 10px; background:white; border-radius:4px; box-shadow:0 0 8px rgba(0,0,0,0.2); font:14px/1.5 'Microsoft YaHei',sans-serif; }}
</style>
</head>
<body>
<div id='map'></div>
<script>
var map = L.map('map', {{ center: [{centerLat}, {centerLon}], zoom: 15, zoomControl: true }});

L.tileLayer('https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{
  attribution: '&copy; <a href=""https://www.openstreetmap.org/copyright"">OpenStreetMap</a>',
  maxZoom: 19
}}).addTo(map);

var geoJsonData = {geoJson};

var geoLayer = L.geoJSON(geoJsonData, {{
  style: {{
    color: '#e64a19',
    weight: 3,
    opacity: 0.9,
    fillColor: '#ff7043',
    fillOpacity: 0.35
  }}
}}).addTo(map);

map.fitBounds(geoLayer.getBounds(), {{ padding: [30, 30] }});

var info = L.control({{ position: 'topright' }});
info.onAdd = function() {{
  var div = L.DomUtil.create('div', 'info');
  div.innerHTML = '<b>{EscapeHtml(_placeName)}</b><br/>{_points.Count} 个坐标点';
  return div;
}};
info.addTo(map);
</script>
</body>
</html>";

        File.WriteAllText(_tempHtmlPath, html, Encoding.UTF8);
    }

    private string BuildGeoJson()
    {
        // 构建闭合环
        var ring = new List<AoiPoint>(_points);
        var first = ring[0];
        var last = ring[^1];
        if (Math.Abs(first.X - last.X) > 0.000001 || Math.Abs(first.Y - last.Y) > 0.000001)
        {
            ring.Add(first);
        }

        var coords = string.Join(",", ring.Select(p => $"[{p.X.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{p.Y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}]"));

        return $"{{\"type\":\"FeatureCollection\",\"features\":[{{\"type\":\"Feature\",\"properties\":{{\"name\":\"{EscapeJson(_placeName)}\"}},\"geometry\":{{\"type\":\"Polygon\",\"coordinates\":[[{coords}]]}}}}]}}";
    }

    private void CleanupTempFile()
    {
        try
        {
            if (File.Exists(_tempHtmlPath))
                File.Delete(_tempHtmlPath);
        }
        catch
        {
            // ignore
        }
    }

    private static string EscapeHtml(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    private static string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
