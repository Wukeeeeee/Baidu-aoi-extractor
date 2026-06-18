# 百度地图 AOI 轮廓提取工具

> 仅用于个人学习与研究。请遵守百度地图服务条款、数据授权要求和相关法律法规。

本项目用于从百度地图页面与接口响应中提取地点 AOI 轮廓，解析为坐标点，并导出为常见 GIS 文件格式。

项目包含两个版本：

- `aoi_extractor.py`：原 Python 命令行版本。
- `BaiduAoiExtractor.Win/`：C# WinForms 图形界面版本，可打包为 Windows 程序。

## Windows 图形版

到 GitHub Releases 下载 Windows 压缩包，例如：

```text
BaiduAoiExtractor_EXE_Final.zip
```

下载后请先完整解压，再双击运行：

```text
BaiduAoiExtractor.Win.exe
```

注意：不要只单独下载 `BaiduAoiExtractor.Win.exe`。图形版依赖同目录下的 DLL、`.playwright` 和浏览器文件夹，必须整个文件夹一起使用。

如果压缩包里没有内置浏览器，首次运行前在程序目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\playwright.ps1 install chromium
```

### 图形版功能

- 输入地点名或地址，搜索百度地图候选结果。
- 将候选地点加入待提取列表。
- 支持从 Excel 批量导入地点。
- 支持设置并发数、随机延迟、显示浏览器窗口和调试日志。
- 导出 CSV、Shapefile、GeoJSON。
- 可输出 Excel 汇总表。
- 坐标输出为 WGS84 / EPSG:4326。
- 内置地图预览，可查看成功提取的轮廓，也可打开已有 GeoJSON。

## Python 版本

安装依赖：

```bash
pip install -r requirements.txt
python -m playwright install chromium
```

运行：

```bash
python aoi_extractor.py
python aoi_extractor.py "广州塔"
python aoi_extractor.py "广州塔" --show
python aoi_extractor.py "广州塔" --debug
```

## 工作原理

核心流程：

1. 使用 Playwright 打开百度地图页面。
2. 在页面内搜索地点。
3. 监听浏览器和百度地图后端的网络响应。
4. 从搜索响应中获取 UID。
5. 从详情响应 `detailConInfo` 中提取 `guoke_geo.geo`。
6. 解析 BD09MC 坐标。
7. 转换为 WGS84。
8. 导出 GIS 文件。

## 输出文件

图形版会按地点导出：

```text
{地点}_轮廓.csv
{地点}_轮廓.shp
{地点}_轮廓.shx
{地点}_轮廓.dbf
{地点}_轮廓.prj
{地点}_轮廓.cpg
{地点}_轮廓.geojson
AOI导出汇总_yyyyMMdd_HHmmss.xlsx
```

## 常见问题

### 为什么下载单个 exe 不能运行？

这是 WinForms + Playwright 程序，运行时需要同目录依赖文件。请下载 Release 里的 zip 包，完整解压后运行。

### 为什么某些地点拿不到 AOI？

可能原因：

- 该地点在百度地图详情里没有 `guoke_geo.geo`。
- 搜索候选不是带 AOI 的具体 POI。
- 网络请求触发风控或返回空详情。
- 百度地图页面结构或接口字段发生变化。

建议打开“显示浏览器窗口”和“输出调试日志”观察实际搜索结果。

## 开发

打开 WinForms 项目：

```text
BaiduAoiExtractor.Win/BaiduAoiExtractor.Win.csproj
```

发布 Windows x64 版本：

```powershell
dotnet publish .\BaiduAoiExtractor.Win\BaiduAoiExtractor.Win.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```
