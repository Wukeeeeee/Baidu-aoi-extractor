"""
百度地图建筑轮廓 & POI 提取工具 (CLI 命令行版)

从百度地图提取建筑轮廓多边形，转换到 WGS84 坐标系，
导出 CSV + GeoJSON + Shapefile 供 GIS 软件使用。

用法:
  python aoi_extractor.py "广州塔"
  python aoi_extractor.py "广州塔" --show
  python aoi_extractor.py "广州塔" --out ./output
"""

import os
import sys
import argparse

# 确保控制台中文输出正常
if sys.platform == "win32" and not os.environ.get("PYCHARM_HOSTED"):
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

from core.crawler import extract_single_aoi
from core.exporter import (
    export_aoi_geojson,
    export_aoi_shp,
    export_aoi_csv,
    export_pois_csv,
    export_pois_shp,
    export_main_poi_csv,
)


def main():
    parser = argparse.ArgumentParser(description="百度地图 AOI 建筑轮廓 & POI 提取工具")
    parser.add_argument("place", nargs="?", help="地点名称 (例如: 广州塔)")
    parser.add_argument("--uid", help="指定百度地图 UID (可选)")
    parser.add_argument("--out", default="output", help="导出目录 (默认: ./output)")
    parser.add_argument("--show", action="store_true", help="显示浏览器窗口")
    parser.add_argument("--debug", action="store_true", help="输出调试日志")
    args = parser.parse_args()

    place = args.place
    if not place:
        place = input("请输入地点名称: ").strip()

    if not place:
        print("错误: 地点名称不能为空")
        sys.exit(1)

    print(f"\n==================================================")
    print(f"  正在提取: {place}")
    print(f"==================================================")

    res = extract_single_aoi(
        place_name=place,
        uid=args.uid,
        headless=not args.show,
        debug=args.debug,
        logger=print
    )

    if not res.get("success"):
        print(f"\n[FAIL] 提取失败: {res.get('error', '未知错误')}")
        sys.exit(1)

    safe_name = "".join([c for c in place if c not in r'\/:*?"<>|']).strip()
    out_dir = os.path.join(args.out, safe_name)
    os.makedirs(out_dir, exist_ok=True)

    pts = res["wgs84_points"]
    pois = res.get("pois", [])
    main_poi = res.get("main_poi")

    # 导出文件
    geojson_path = os.path.join(out_dir, f"{safe_name}_AOI范围.geojson")
    shp_path = os.path.join(out_dir, f"{safe_name}_AOI范围.shp")
    csv_path = os.path.join(out_dir, f"{safe_name}_AOI范围.csv")

    export_aoi_geojson(pts, safe_name, geojson_path)
    export_aoi_shp(pts, safe_name, shp_path)
    export_aoi_csv(pts, csv_path)

    if pois:
        poi_csv = os.path.join(out_dir, f"{safe_name}_POI点.csv")
        poi_shp = os.path.join(out_dir, f"{safe_name}_POI点.shp")
        export_pois_csv(pois, poi_csv)
        export_pois_shp(pois, poi_shp)

    if main_poi and main_poi.get("longitude") is not None:
        main_csv = os.path.join(out_dir, f"{safe_name}_该点POI.csv")
        export_main_poi_csv(safe_name, main_poi["longitude"], main_poi["latitude"], main_csv)

    print(f"\n==================================================")
    print(f"  [DONE] 提取完成!")
    print(f"  AOI 顶点数: {len(pts)}")
    print(f"  周边 POI 数: {len(pois)}")
    print(f"  输出目录: {os.path.abspath(out_dir)}")
    print(f"==================================================")


if __name__ == "__main__":
    main()
