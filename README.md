<p align="center">
  <img src="https://img.shields.io/badge/Python-3.10%2B-blue" alt="Python">
  <img src="https://img.shields.io/badge/Playwright-Browser%20Automation-green" alt="Playwright">
  <img src="https://img.shields.io/badge/Output-CSV%20%7C%20Shapefile-orange" alt="Output">
</p>

<h1 align="center">百度地图AOI轮廓获取工具</h1>
<p align="center"><b>Baidu Maps AOI Building Outline Extractor</b></p>

<p align="center">自动提取百度地图建筑轮廓 / Extract building outlines from Baidu Maps</p>

---

> **免责声明 / Disclaimer**
>
> 仅用于个人学习与研究 / For educational and research purposes only.
> 请遵守百度地图服务条款及相关法律法规 / Respect Baidu Maps ToS and applicable laws.

---

## 效果 / Preview

```
地点 (Place)                   坐标点数 (Points)
────────────────────────────────────────────
广州第八十九中学                     83
中南林业科技大学                   1900+
长沙黄花国际机场                   2800+
广州塔                              200+
```

输出为 **WGS84 (EPSG:4326)**，含 PRJ 文件，可直接在 ArcGIS / QGIS 里打开。
Outputs in **WGS84 (EPSG:4326)** with PRJ, ready for ArcGIS / QGIS.

---

## 安装 / Install

```bash
pip install playwright transbigdata geopandas
```

安装浏览器驱动 / Install browser driver:

```bash
# 有 Chrome 用这个 / If you have Chrome
python -m playwright install chrome

# 没 Chrome 用这个 / Otherwise
python -m playwright install chromium
```

## 用法 / Usage

```bash
# 交互模式 / Interactive
python aoi_extractor.py

# 直接指定 / Direct
python aoi_extractor.py "广州塔"

# 显示浏览器窗口 / Show browser
python aoi_extractor.py "广州塔" --show

# 调试模式 / Debug
python aoi_extractor.py "广州塔" --debug
```

## 输出 / Output

```
{place}_轮廓.csv    — 坐标点列表 / Point list (X=lon, Y=lat)
{place}_轮廓.shp    — 面要素 Shapefile（含 .shx .dbf .prj .cpg）
```

## 工作原理 / How It Works

```
输入地名 / Enter name
  ↓
Playwright 打开 map.baidu.com / Open Baidu Maps
  ↓
搜索 → 拦截 API 拿 UID / Search → intercept API → get UID
  ↓
请求详情 API → 提取轮廓 / Request detail API → extract outline
  ↓
BD09MC → BD09 → GCJ02 → WGS84（三层坐标转换 / CRS transform）
  ↓
导出 CSV + Shapefile / Export
```

## 项目结构 / Project Structure

```
baidu-aoi-extractor/
├── aoi_extractor.py         ← 主程序 / Main
├── requirements.txt
├── README.md
├── LICENSE
├── .gitignore
└── archive/
    ├── v1_request_demo.py   ← 早期原型 / Early prototype
    └── v2_semi_auto.py      ← 半自动版 / Semi-automatic version
```

## 常见问题 / FAQ

**拿不到轮廓？/ No outline returned?**
- 该地点可能没有轮廓数据 / Place may not have outline data
- 试试 `--show` 观察浏览器 / Try `--show` to watch the browser
- 手动去 map.baidu.com 确认 / Check manually on map.baidu.com

**Playwright 启动失败？/ Launch failed?**
```bash
python -m playwright install chromium
```

**为什么三层坐标转换？/ Why 3-layer CRS?**
国内地图法定加密，百度 BD09MC → GCJ02 → 国际标准 WGS84。
Chinese regulations require map encryption: BD09MC → GCJ02 → WGS84.
