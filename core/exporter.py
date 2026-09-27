"""
GIS 多格式导出模块
支持 GeoJSON, Shapefile (shp/shx/dbf/prj/cpg), CSV, Excel 汇总表导出
"""

import os
import csv
import json
import time
from typing import List, Tuple, Dict, Any, Optional
import pandas as pd
import geopandas as gpd
from shapely.geometry import Polygon, Point


WGS84_PRJ_CONTENT = (
    'GEOGCS["GCS_WGS_1984",DATUM["D_WGS_1984",'
    'SPHEROID["WGS_1984",6378137.0,298.257223563]],'
    'PRIMEM["Greenwich",0.0],'
    'UNIT["Degree",0.0174532925199433]]'
)


def ensure_cpg_and_prj(shp_path: str):
    """确保 SHP 同目录下生成 .cpg (UTF-8) 与 .prj (WGS84) 文件，防止 GIS 乱码或无投影"""
    base, _ = os.path.splitext(shp_path)
    cpg_path = base + ".cpg"
    prj_path = base + ".prj"
    try:
        with open(cpg_path, "w", encoding="utf-8") as f:
            f.write("UTF-8")
    except Exception:
        pass

    try:
        with open(prj_path, "w", encoding="utf-8") as f:
            f.write(WGS84_PRJ_CONTENT)
    except Exception:
        pass


def export_aoi_geojson(points: List[Tuple[float, float]], place_name: str, output_path: Optional[str] = None) -> Optional[Dict[str, Any]]:
    """生成或保存 AOI GeoJSON"""
    if not points or len(points) < 3:
        return None

    # 闭合多边形检查
    pts = list(points)
    if pts[0] != pts[-1]:
        pts.append(pts[0])

    poly = Polygon(pts)
    gdf = gpd.GeoDataFrame(
        [{"name": place_name, "type": "AOI_Outline", "points_count": len(points)}],
        geometry=[poly],
        crs="EPSG:4326"
    )
    geojson_dict = json.loads(gdf.to_json())

    if output_path:
        os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(geojson_dict, f, ensure_ascii=False, indent=2)

    return geojson_dict


def export_aoi_shp(points: List[Tuple[float, float]], place_name: str, output_path: str) -> bool:
    """保存 AOI 为 Shapefile 面要素"""
    if not points or len(points) < 3:
        return False

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    pts = list(points)
    if pts[0] != pts[-1]:
        pts.append(pts[0])

    poly = Polygon(pts)
    gdf = gpd.GeoDataFrame(
        [{"name": place_name[:50], "pts_num": len(points)}],
        geometry=[poly],
        crs="EPSG:4326"
    )

    for attempt in range(3):
        try:
            gdf.to_file(output_path, encoding="utf-8")
            ensure_cpg_and_prj(output_path)
            return True
        except PermissionError:
            time.sleep(1)
        except Exception:
            break
    return False


def export_aoi_csv(points: List[Tuple[float, float]], output_path: str) -> bool:
    """保存 AOI 坐标点到 CSV"""
    if not points:
        return False
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    try:
        with open(output_path, "w", encoding="utf-8-sig", newline="") as f:
            writer = csv.writer(f)
            writer.writerow(["Index", "Longitude", "Latitude"])
            for idx, (lon, lat) in enumerate(points, 1):
                writer.writerow([idx, lon, lat])
        return True
    except Exception:
        return False


def export_main_poi_csv(name: str, lon: float, lat: float, output_path: str) -> bool:
    """只导出当前地点自身一行 CSV: name,latitude,longitude"""
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    try:
        with open(output_path, "w", encoding="utf-8-sig", newline="") as f:
            writer = csv.writer(f)
            writer.writerow(["name", "latitude", "longitude"])
            writer.writerow([name, lat, lon])
        return True
    except Exception:
        return False


def export_pois_csv(pois: List[Dict[str, Any]], output_path: str) -> bool:
    """导出周边 POI 点位列表到 CSV"""
    if not pois:
        return False
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    try:
        df = pd.DataFrame(pois)
        cols = ["name", "latitude", "longitude", "address", "tag", "uid"]
        avail_cols = [c for c in cols if c in df.columns]
        df[avail_cols].to_csv(output_path, index=False, encoding="utf-8-sig")
        return True
    except Exception:
        return False


def export_pois_shp(pois: List[Dict[str, Any]], output_path: str) -> bool:
    """导出周边 POI 点位到 Shapefile 点要素"""
    if not pois:
        return False
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    try:
        records = []
        geoms = []
        for p in pois:
            lon = p.get("longitude")
            lat = p.get("latitude")
            if lon is not None and lat is not None:
                records.append({
                    "name": str(p.get("name", ""))[:50],
                    "address": str(p.get("address", ""))[:80],
                    "tag": str(p.get("tag", ""))[:30],
                    "uid": str(p.get("uid", ""))[:30]
                })
                geoms.append(Point(float(lon), float(lat)))

        if not geoms:
            return False

        gdf = gpd.GeoDataFrame(records, geometry=geoms, crs="EPSG:4326")
        gdf.to_file(output_path, encoding="utf-8")
        ensure_cpg_and_prj(output_path)
        return True
    except Exception:
        return False


def export_summary_excel(records: List[Dict[str, Any]], output_path: str) -> bool:
    """生成批量任务 Excel 汇总表"""
    if not records:
        return False
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    try:
        df = pd.DataFrame(records)
        df.to_excel(output_path, index=False, engine="openpyxl")
        return True
    except Exception:
        # Fallback to csv if openpyxl fails
        try:
            csv_path = output_path.replace(".xlsx", ".csv")
            df = pd.DataFrame(records)
            df.to_csv(csv_path, index=False, encoding="utf-8-sig")
            return True
        except Exception:
            return False
