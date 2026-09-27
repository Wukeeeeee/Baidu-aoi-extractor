# 百度地图 AOI 建筑轮廓 & POI 提取工具 / Baidu Map AOI & POI Extractor

<p align="center">
  <img src="https://img.shields.io/badge/Python-3.9+-3776AB?style=flat-square&logo=python&logoColor=white" alt="Python Version" />
  <img src="https://img.shields.io/badge/Playwright-Automation-2EAD33?style=flat-square&logo=playwright&logoColor=white" alt="Playwright" />
  <img src="https://img.shields.io/badge/FastAPI-Framework-009688?style=flat-square&logo=fastapi&logoColor=white" alt="FastAPI" />
  <img src="https://img.shields.io/badge/CRS-WGS84%20(EPSG:4326)-212529?style=flat-square" alt="WGS84" />
  <img src="https://img.shields.io/badge/License-MIT-black?style=flat-square" alt="License" />
</p>

<p align="center">
  <b>简体中文</b> | <a href="#english">English</a>
</p>

---

## 免责声明 / Disclaimer

1. **学术与研究用途**：本项目仅供地理信息系统（GIS）空间数据结构学习、坐标系纠偏算法研究及 Python 自动化技术交流使用。
2. **遵守相关条款**：用户在使用本项目时，应严格遵守相关服务商的服务条款、反爬虫协议及相关法律法规。严禁将本项目用于任何商业牟利、未经授权的大规模爬取或任何破坏性活动。
3. **责任界定**：本项目为纯技术研究开源项目，不内置、不提供、不存储任何地图矢量数据。使用本项目所产生的一切后果由使用者自行承担，开发者不承担任何直接或连带法律责任。

---

## 简体中文

### 项目简介

**Baidu AOI Extractor** 是一个现代、轻量、高可用的地理空间边界（AOI，Area of Interest）与 POI 数据提取工具。

基于 Python 驱动与 Playwright 自动化拦截技术，无需逆向破解底层动态加密签名，即可从公开页面精准提取建筑物、商圈、学校、园区、景区的 **AOI 轮廓多边形** 与 **周边 POI 坐标**，并自动完成高精度坐标转换（`BD09MC` $\rightarrow$ `BD09` $\rightarrow$ `GCJ02` $\rightarrow$ `WGS84 (EPSG:4326)`），一键导出为标准的 GIS 矢量与表格文件。

### 核心特性

- **现代双栏工作台 (Web UI)**：极简高密度专业 GIS 工具设计，内置 Leaflet.js 交互式地图（支持 CARTO 灰度、OSM、卫星图切换）。
- **全国毫秒级智能联想**：支持地点模糊检索与实时 Suggest 补全（支持“广州站”、“长沙南站”等枢纽与别名）。
- **表格批量导入与解析**：支持 `.xlsx` / `.xls` / `.csv` 一键导入、自动列名识别与队列批量提取。
- **严谨几何与坐标解算**：
  - 自动处理多环（Multi-Polygon）前缀与正则分环清洗，彻底杜绝极点拉线 Bug。
  - 全自动坐标转换链，输出标准 WGS84（EPSG:4326）通用坐标。
- **全格式 GIS 导出**：
  - **AOI 建筑边界**：Shapefile（面要素，含 `.prj` 投影与 `.cpg` UTF-8）、GeoJSON、CSV
  - **周边 POI 点集**：Shapefile（点要素）、CSV
  - **核心地点坐标**：点位 CSV
  - **批量统计报表**：一键生成 Excel 汇总表（`.xlsx`）
- **双模运行**：支持 Web 图形界面与 CLI 命令行自动化批量处理。

### 快速开始

#### 1. 克隆仓库与安装依赖

```bash
git clone https://github.com/your-username/baidu-aoi-extractor.git
cd baidu-aoi-extractor

# 安装依赖
pip install -r requirements.txt

# 安装 Playwright 浏览器内核
python -m playwright install chromium
```

#### 2. 启动 Web 图形界面 (推荐)

在 Windows 上双击 `start.bat`，或在终端执行：

```bash
python app.py
```

服务启动后将自动在默认浏览器打开 `http://127.0.0.1:8765`。

#### 3. CLI 命令行调用

```bash
# 单地点提取
python aoi_extractor.py "广州塔"

# 显示浏览器执行（非无头模式）
python aoi_extractor.py "广州塔" --show

# 自定义导出路径
python aoi_extractor.py "清华大学" --out ./my_outputs
```

### 目录结构

```text
baidu-aoi-extractor/
├── app.py              # FastAPI Web 服务与 API 入口
├── aoi_extractor.py    # CLI 命令行处理入口
├── start.bat           # Windows 一键启动脚本
├── core/               # 核心算法与爬虫模块
│   ├── converter.py    # 坐标系纠偏、几何脱壳与 POI 解析
│   ├── crawler.py      # Playwright 自动化与网络拦截
│   └── exporter.py     # Shapefile / GeoJSON / CSV / Excel 导出引擎
├── web/                # 前端界面
│   └── index.html      # 现代化单页工作台 (Tailwind + Leaflet)
├── output/             # 导出文件默认存储目录 (默认 gitignore)
├── requirements.txt    # 依赖声明
├── LICENSE             # MIT 开源协议
└── README.md           # 中英文文档
```

---

<span id="english"></span>

## English

### Introduction

**Baidu AOI Extractor** is a modern, lightweight, and robust geospatial tool designed for extracting **AOI (Area of Interest) building outlines** and **surrounding POIs (Points of Interest)** from Baidu Map.

Powered by Python and Playwright browser automation, it intercepts structured geospatial payload responses without requiring manual reverse-engineering of dynamic tokens. Coordinates are automatically transformed across projection layers (`BD09MC` $\rightarrow$ `BD09` $\rightarrow$ `GCJ02` $\rightarrow$ `WGS84 (EPSG:4326)`) and exported directly into standard GIS vector and tabular formats.

### Key Features

- **Modern 2-Panel Web Workbench**: Clean, high-density, professional desktop GIS interface with interactive Leaflet.js map preview (CARTO Light, OSM, and Satellite basemaps).
- **Nationwide Fast Suggestion**: Sub-100ms keyword auto-complete and multi-region transit hub resolution (e.g., "Guangzhou Railway Station", "Changsha South Station").
- **Batch Table Import**: Import `.xlsx`, `.xls`, or `.csv` files with automatic column recognition and queue management.
- **Robust Geometric Parsing**:
  - Regularized ring parsing and prefix stripping to completely eliminate polar coordinate glitches.
  - Multi-layer transformation pipeline outputting standard WGS84 coordinates.
- **Comprehensive GIS Export Formats**:
  - **AOI Polygons**: Shapefile (Polygon with `.prj` and `.cpg`), GeoJSON, CSV.
  - **Surrounding POIs**: Shapefile (Point), CSV.
  - **Main Landmark POI**: Single-row CSV.
  - **Batch Summary**: Consolidated `.xlsx` report.
- **Dual Modes**: Intuitive Web UI and scriptable CLI automation.

### Quick Start

#### 1. Clone & Install Dependencies

```bash
git clone https://github.com/your-username/baidu-aoi-extractor.git
cd baidu-aoi-extractor

# Install dependencies
pip install -r requirements.txt

# Install Playwright browser binaries
python -m playwright install chromium
```

#### 2. Launch Web UI (Recommended)

Double-click `start.bat` on Windows, or run:

```bash
python app.py
```

The application will automatically launch and open `http://127.0.0.1:8765` in your default browser.

#### 3. CLI Usage

```bash
# Extract single location
python aoi_extractor.py "Canton Tower"

# Run with visible browser window
python aoi_extractor.py "Canton Tower" --show

# Specify custom output directory
python aoi_extractor.py "Tsinghua University" --out ./my_outputs
```

### Export Output Structure

```text
output/
  LocationName/
    LocationName_AOI范围.geojson
    LocationName_AOI范围.shp
    LocationName_AOI范围.shx
    LocationName_AOI范围.dbf
    LocationName_AOI范围.prj
    LocationName_AOI范围.cpg
    LocationName_AOI范围.csv
    LocationName_POI点.shp
    LocationName_POI点.shx
    LocationName_POI点.dbf
    LocationName_POI点.prj
    LocationName_POI点.cpg
    LocationName_POI点.csv
    LocationName_该点POI.csv
  AOI导出汇总_YYYYMMDD_HHMMSS.xlsx
```

---

## 许可证 / License

本项目基于 [MIT License](LICENSE) 许可发布。
Released under the [MIT License](LICENSE).
