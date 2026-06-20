# 百度地图 AOI 边界提取工具

> 仅用于个人学习与研究。请遵守百度地图服务条款、数据授权要求和相关法律法规。

本项目用于从百度地图页面和后端响应中提取地点 AOI 边界、POI 点位，并导出为常见 GIS / 表格文件。

## 当前版本

当前发布版本：`v1.1`

Windows 图形版发布文件夹：

```text
release-win/
```

进入该文件夹后双击运行：

```text
BaiduAoiExtractor.Win.exe
```

注意：不要只单独复制 exe。`release-win` 包含运行所需的 DLL、Playwright、浏览器文件和 .NET 运行时，需要整个文件夹一起使用。

由于 `release-win` 文件夹较大，仓库使用 Git LFS 存放发布文件。克隆后如果发现文件很小或无法运行，请先安装 Git LFS，然后执行：

```bash
git lfs install
git lfs pull
```

## 图形版主要功能

- 输入地点名或地址，搜索百度地图候选结果。
- 支持 Excel 导入地点列表，先进入“Excel导入预览”，再手动添加选中或全部添加到待提取任务。
- Excel 没有 UID 时，可勾选“无UID时自动选第一个候选”，提取时自动搜索并选择第一条候选。
- 支持批量提取 AOI 边界。
- 支持从后端响应中读取周围 POI 点位。
- 右侧地图预览 AOI 边界。
- 支持导出当前选中结果：
  - `导出AOI范围`：导出 AOI 边界 CSV / GeoJSON / Shapefile。
  - `导出周围POI点位`：导出周围 POI 点 CSV / Shapefile。
  - `导出该点POI`：只导出当前地点本身一行 CSV，字段为 `name,latitude,longitude`。
- 批量任务结束后可输出 Excel 汇总表。
- 坐标输出为 WGS84 / EPSG:4326。

## 输出结构

批量自动导出会在输出目录生成 AOI 边界文件：

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

在运行结果中选中某条记录后，点击导出按钮会创建以地点名命名的文件夹：

```text
输出目录/
  地点名/
    地点名_AOI范围.csv
    地点名_AOI范围.geojson
    地点名_AOI范围.shp/.shx/.dbf/.prj/.cpg
    地点名_POI点.csv
    地点名_POI点.shp/.shx/.dbf/.prj/.cpg
    地点名_该点POI.csv
```

`该点POI.csv` 只包含三列：

```csv
name,latitude,longitude
```

## Python 版

安装依赖：

```bash
pip install -r requirements.txt
python -m playwright install chromium
```

运行示例：

```bash
python aoi_extractor.py
python aoi_extractor.py "广州塔"
python aoi_extractor.py "广州塔" --show
python aoi_extractor.py "广州塔" --debug
```

## 工作原理

1. 使用 Playwright 打开百度地图页面。
2. 在页面内搜索地点。
3. 监听浏览器和百度地图后端网络响应。
4. 从搜索响应中获取 UID 和候选 POI。
5. 从详情响应 `detailConInfo` 中提取 `guoke_geo.geo`。
6. 解析 BD09MC 坐标。
7. 转换为 WGS84。
8. 导出 CSV / GeoJSON / Shapefile / Excel。

## 常见问题

### 为什么只下载 exe 不能运行？

这是 WinForms + Playwright 程序，运行时需要同目录依赖文件、浏览器文件和 .NET 运行时。请使用完整的 `release-win` 文件夹。

### 为什么某些地点拿不到 AOI？

可能原因：

- 该地点在百度地图详情里没有 `guoke_geo.geo`。
- 搜索候选不是带 AOI 的具体 POI。
- 网络请求触发风控或返回空详情。
- 百度地图页面结构或接口字段发生变化。

建议打开“显示浏览器窗口”和“输出调试日志”观察实际搜索结果。

## 版本更新日志

### v1.1 - 2026-06-21

本版本基于上一版 Final 图形界面继续改进，重点增强 Excel 批量导入、POI 点位导出和结果区按钮布局。

新增内容：

- 新增 `Excel导入预览` 区域：导入 Excel 后不会立即进入待提取任务，可 `添加选中`、`全部添加` 或 `清空预览`。
- 新增 `无UID时自动选第一个候选` 选项：对没有 UID 的 Excel 任务，提取前自动搜索百度地图候选并选择第一条。
- 新增 POI 点位解析：从百度地图后端 JSON 中递归读取常见 POI 坐标字段，并统一转换为 WGS84。
- 新增结果区导出按钮：`导出AOI范围`、`导出周围POI点位`、`导出该点POI`。
- 新增 `该点POI.csv` 导出，只输出当前地点本身一行，字段固定为 `name,latitude,longitude`。
- 新增周围 POI 点位 CSV / Shapefile 导出。

界面改进：

- `运行控制` 移到底部。
- `导入 Excel` 移入 `Excel导入预览` 区域。
- `开始提取` 按钮增大并强化颜色。
- 结果区按钮从一行改为两行，避免文字显示不全。
- 结果列表信息中显示已读取的 POI 数量。

与上一版的主要差别：

- 上一版 Excel 导入会直接加入任务；v1.1 改为先预览再添加。
- 上一版没有自动选择候选开关；v1.1 可对无 UID 任务自动选择第一条候选。
- 上一版主要导出 AOI 边界；v1.1 增加周围 POI 点位和该点 POI 导出。
- 上一版结果区按钮较少；v1.1 增加导出按钮并改成两行布局。
- 上一版发布目录名称较长；v1.1 改为 `release-win`。

## 开发

WinForms 项目文件：

```text
BaiduAoiExtractor.Win/BaiduAoiExtractor.Win.csproj
```

发布 Windows x64 Final 文件夹：

```powershell
dotnet publish .\BaiduAoiExtractor.Win\BaiduAoiExtractor.Win.csproj -c Release -r win-x64 --self-contained true -o .\release-win
```
