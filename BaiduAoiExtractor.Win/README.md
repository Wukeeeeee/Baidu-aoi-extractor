# 百度地图 AOI 轮廓提取器（C# 版）

这是基于仓库里 `aoi_extractor.py` 的 C# WinForms 版本，原 Python 文件未修改。

## 功能

- 像百度搜索栏一样先输入地址或地点名，再搜索候选结果。
- 可以从候选结果里选择具体地址，程序会优先使用该结果的 UID。
- 支持从 Excel 导入地点或地址，默认读取第一列，也支持表头：地点、地址、名称、name、address、place、query、uid。
- 支持并发提取，默认 1，最高 5。
- 支持随机延迟，降低请求频率。
- 支持导出 CSV、Shapefile、GeoJSON。
- 支持输出 Excel 汇总，每个成功地点会有一个坐标表。
- 导出坐标为 WGS84 / EPSG:4326。

## 使用

1. 打开发布目录里的 `BaiduAoiExtractor.Win.exe`。
2. 在顶部输入地址或地点名，点击“搜索候选”。
3. 在左侧候选列表选择具体地址，点击“添加选中”；也可以直接点击“导入Excel”批量导入。
4. 选择输出目录，按需设置并发数、随机延迟、GeoJSON 和 Excel 汇总。
5. 点击右上角绿色的“开始提取”。

导出文件格式：

- `{地点}_轮廓.csv`
- `{地点}_轮廓.shp`
- `{地点}_轮廓.shx`
- `{地点}_轮廓.dbf`
- `{地点}_轮廓.prj`
- `{地点}_轮廓.cpg`
- `{地点}_轮廓.geojson`
- `AOI导出汇总_yyyyMMdd_HHmmss.xlsx`

## 浏览器依赖

程序会优先使用系统 Chrome。没有 Chrome 时，会尝试使用 Playwright Chromium。

如果运行时报浏览器启动失败，请在发布目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\playwright.ps1 install chromium
```

## 合规提示

请仅用于学习、研究或你有权处理的数据场景，并遵守百度地图服务条款及相关法律法规。
