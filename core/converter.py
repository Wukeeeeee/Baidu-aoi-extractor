"""
坐标系统转换与空间几何解析模块
支持 BD09MC (百度墨卡托) -> BD09 (百度经纬度) -> GCJ02 (火星坐标) -> WGS84 (标准地理坐标)
"""

import math
import re
from typing import List, Tuple, Dict, Any, Optional

# 常量定义
PI = math.pi
X_PI = PI * 3000.0 / 180.0
A = 6378245.0  # 椭球长半轴
EE = 0.00669342162296594323  # 椭球偏心率平方
MC_RADIUS = 6378137.0


def _out_of_china(lon: float, lat: float) -> bool:
    """判断是否在中国境外"""
    return not (72.004 <= lon <= 137.8347 and 0.8293 <= lat <= 55.8271)


def _transform_lat(x: float, y: float) -> float:
    ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * math.sqrt(abs(x))
    ret += (20.0 * math.sin(6.0 * x * PI) + 20.0 * math.sin(2.0 * x * PI)) * 2.0 / 3.0
    ret += (20.0 * math.sin(y * PI) + 40.0 * math.sin(y / 3.0 * PI)) * 2.0 / 3.0
    ret += (160.0 * math.sin(y / 12.0 * PI) + 320 * math.sin(y * PI / 30.0)) * 2.0 / 3.0
    return ret


def _transform_lon(x: float, y: float) -> float:
    ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * math.sqrt(abs(x))
    ret += (20.0 * math.sin(6.0 * x * PI) + 20.0 * math.sin(2.0 * x * PI)) * 2.0 / 3.0
    ret += (20.0 * math.sin(x * PI) + 40.0 * math.sin(x / 3.0 * PI)) * 2.0 / 3.0
    ret += (150.0 * math.sin(x / 12.0 * PI) + 300.0 * math.sin(x / 30.0 * PI)) * 2.0 / 3.0
    return ret


def bd09mc_to_bd09(mc_x: float, mc_y: float) -> Tuple[float, float]:
    """百度墨卡托平面坐标 (BD09MC) 转 百度经纬度 (BD09)"""
    try:
        import transbigdata
        lon, lat = transbigdata.bd09mctobd09(mc_x, mc_y)
        return float(lon), float(lat)
    except Exception:
        pass

    lon = (mc_x / MC_RADIUS) * 180.0 / PI
    lat = (2.0 * math.atan(math.exp(mc_y / MC_RADIUS)) - PI / 2.0) * 180.0 / PI
    return lon, lat


def bd09_to_gcj02(bd_lon: float, bd_lat: float) -> Tuple[float, float]:
    """百度经纬度 (BD09) 转 火星坐标系 (GCJ02)"""
    x = bd_lon - 0.0065
    y = bd_lat - 0.006
    z = math.sqrt(x * x + y * y) - 0.00002 * math.sin(y * X_PI)
    theta = math.atan2(y, x) - 0.000003 * math.cos(x * X_PI)
    gcj_lon = z * math.cos(theta)
    gcj_lat = z * math.sin(theta)
    return gcj_lon, gcj_lat


def gcj02_to_wgs84(gcj_lon: float, gcj_lat: float) -> Tuple[float, float]:
    """火星坐标系 (GCJ02) 转 WGS84 国际标准经纬度"""
    if _out_of_china(gcj_lon, gcj_lat):
        return gcj_lon, gcj_lat

    dlat = _transform_lat(gcj_lon - 105.0, gcj_lat - 35.0)
    dlon = _transform_lon(gcj_lon - 105.0, gcj_lat - 35.0)
    radlat = gcj_lat / 180.0 * PI
    magic = math.sin(radlat)
    magic = 1 - EE * magic * magic
    sqrtmagic = math.sqrt(magic)
    dlat = (dlat * 180.0) / ((A * (1 - EE)) / (magic * sqrtmagic) * PI)
    dlon = (dlon * 180.0) / (A / sqrtmagic * math.cos(radlat) * PI)
    wgs_lat = gcj_lat - dlat
    wgs_lon = gcj_lon - dlon
    return wgs_lon, wgs_lat


def bd09mc_to_wgs84(mc_x: float, mc_y: float) -> Tuple[float, float]:
    """全流程转换: BD09MC -> BD09 -> GCJ02 -> WGS84"""
    bd_lon, bd_lat = bd09mc_to_bd09(mc_x, mc_y)
    gcj_lon, gcj_lat = bd09_to_gcj02(bd_lon, bd_lat)
    wgs_lon, wgs_lat = gcj02_to_wgs84(gcj_lon, gcj_lat)
    return round(wgs_lon, 6), round(wgs_lat, 6)


def bd09_to_wgs84(bd_lon: float, bd_lat: float) -> Tuple[float, float]:
    """BD09 经纬度 -> WGS84"""
    gcj_lon, gcj_lat = bd09_to_gcj02(bd_lon, bd_lat)
    wgs_lon, wgs_lat = gcj02_to_wgs84(gcj_lon, gcj_lat)
    return round(wgs_lon, 6), round(wgs_lat, 6)


def smart_convert_coords(x: float, y: float) -> Tuple[float, float]:
    """
    智能自适应转换各类百度坐标返回值为 WGS84
    """
    fx, fy = float(x), float(y)
    # 1. 正常经纬度 (-180~180, -90~90)
    if -180 <= fx <= 180 and -90 <= fy <= 90:
        return bd09_to_wgs84(fx, fy)
    # 2. 标准 BD09MC 墨卡托米制坐标 (如 12000000, 2600000)
    if abs(fx) > 1000000:
        return bd09mc_to_wgs84(fx, fy)
    # 3. 经纬度放大 100 倍 (如 11331.17, 2310.9)
    if 5000 <= abs(fx) <= 20000 and 0 <= abs(fy) <= 10000:
        return bd09_to_wgs84(fx / 100.0, fy / 100.0)
    # 4. 经纬度放大 100000 倍 (如 11331903, 2310933)
    if 5000000 <= abs(fx) <= 20000000 and 0 <= abs(fy) <= 10000000:
        return bd09_to_wgs84(fx / 100000.0, fy / 100000.0)
    # 5. 墨卡托缩小 100 倍 (如 126157.58)
    return bd09mc_to_wgs84(fx * 100.0, fy * 100.0)


def parse_geo_to_points(geo_str: str) -> List[Tuple[float, float]]:
    """
    解析百度地图返回的 geo 字符串为墨卡托点列表
    常见格式:
      "4|bbox|1-12581487.29,3253725.72,12581483.76,3253588.52;..."
      "1|1|a12623123.12,3456789.01,12623145.23,3456790.12,...;"
      "1|point|12623123.12,3456789.01;"
    """
    if not geo_str or not isinstance(geo_str, str):
        return []

    parts = geo_str.split("|")
    target_str = parts[2] if len(parts) >= 3 else geo_str

    points = []
    # 百度多边形各环或要素以分号分隔
    rings = target_str.split(";")
    for ring in rings:
        ring = ring.strip()
        if not ring:
            continue
        # 去掉环前缀标识 (如 '1-', '2-', 'a', '0-' 等)
        if "-" in ring:
            ring = ring.split("-", 1)[1]
        elif ring and ring[0].isalpha():
            ring = ring[1:]

        # 匹配所有非负/浮点数坐标
        tokens = re.findall(r"\d+(?:\.\d+)?", ring)
        for i in range(0, len(tokens) - 1, 2):
            try:
                x = float(tokens[i])
                y = float(tokens[i + 1])
                points.append((x, y))
            except Exception:
                continue

    return points


def convert_points_to_wgs84(mc_points: List[Tuple[float, float]]) -> List[Tuple[float, float]]:
    """将 BD09MC 点列表批量转换为 WGS84 经纬度列表"""
    wgs_points = []
    for x, y in mc_points:
        wgs_points.append(smart_convert_coords(x, y))
    return wgs_points


def extract_pois_from_raw_json(data: Any) -> List[Dict[str, Any]]:
    """
    从百度地图搜索/详情 JSON 响应中递归提取所有 POI 点位
    """
    results = []
    seen = set()

    def _traverse(node):
        if isinstance(node, dict):
            name = node.get("name") or node.get("std_tag") or ""
            uid = node.get("uid") or node.get("primary_uid") or ""
            addr = node.get("addr") or node.get("address") or ""
            di_tag = node.get("di_tag") or node.get("tag") or ""

            px = node.get("x") or node.get("diPointX")
            py = node.get("y") or node.get("diPointY")

            if px is not None and py is not None and name:
                try:
                    wgs_lon, wgs_lat = smart_convert_coords(float(px), float(py))
                    key = f"{name}_{wgs_lon}_{wgs_lat}"
                    if key not in seen:
                        seen.add(key)
                        results.append({
                            "name": str(name),
                            "uid": str(uid),
                            "address": str(addr),
                            "tag": str(di_tag),
                            "longitude": wgs_lon,
                            "latitude": wgs_lat
                        })
                except Exception:
                    pass

            for v in node.values():
                _traverse(v)

        elif isinstance(node, list):
            for item in node:
                _traverse(item)

    _traverse(data)
    return results


# ---------------------------------------------------------------------------
# 住宅小区（Community AOI）支持
#
# 实测结论（2026-10，曹杨新村/望京样本）：
#   - 住宅小区的 detail 响应里 guoke_geo.geo 只有一个中心点，没有边界；
#   - 真实建筑轮廓在 ext.detail_info.guoke_geo_bud.bud_geom，标准 WKT（POLYGON），
#     坐标为 BD09MC 墨卡托，空格分隔；
#   - guoke_geo_bud.belong_aoi 是该楼所属 AOI 组的 uid，
#     用它再查一次 detail 可拿到整个小区的全部建筑。
# ---------------------------------------------------------------------------


def extract_buildings_from_detail_json(data: Any) -> List[Dict[str, Any]]:
    """
    递归收集 detail 响应中所有 guoke_geo_bud 建筑条目（WKT bud_geom）。
    无论响应是单小区详情还是 AOI 组详情，结构都可能是嵌套 dict/list，统一递归。
    """
    results: List[Dict[str, Any]] = []
    seen = set()

    def _walk(node: Any) -> None:
        if isinstance(node, dict):
            bud = node.get("guoke_geo_bud")
            if isinstance(bud, dict) and bud.get("bud_geom"):
                key = bud.get("face_id") or str(bud.get("bud_geom"))[:80]
                if key not in seen:
                    seen.add(key)
                    results.append({
                        "face_id": str(bud.get("face_id") or ""),
                        "wkt": str(bud.get("bud_geom") or ""),
                        "height": str(bud.get("height") or ""),
                        "belong_aoi": str(bud.get("belong_aoi") or ""),
                    })
            for v in node.values():
                _walk(v)
        elif isinstance(node, list):
            for item in node:
                _walk(item)

    _walk(data)
    return results


def extract_belong_aoi(data: Any) -> str:
    """递归查找响应中的 belong_aoi（AOI 组 uid），即便没有 bud_geom 也要拿到它"""
    found = ""

    def _walk(node: Any) -> None:
        nonlocal found
        if found:
            return
        if isinstance(node, dict):
            bud = node.get("guoke_geo_bud")
            if isinstance(bud, dict) and bud.get("belong_aoi"):
                found = str(bud["belong_aoi"])
                return
            for v in node.values():
                _walk(v)
        elif isinstance(node, list):
            for item in node:
                _walk(item)

    _walk(data)
    return found


def parse_wkt_polygon(wkt: str) -> List[Tuple[float, float]]:
    """
    解析 POLYGON ((x y, x y, ...)) 为 MC 坐标点列表。
    注意 WKT 是「空格分隔 xy、逗号分隔点」，与百度自定义 geo 字符串不同，不能混用。
    MULTIPOLYGON 时取第一个面。
    """
    if not wkt or not isinstance(wkt, str):
        return []
    m = re.search(r"\(\(([^)]+)\)", wkt)
    if not m:
        return []
    pts: List[Tuple[float, float]] = []
    for pair in m.group(1).split(","):
        parts = pair.strip().split()
        if len(parts) >= 2:
            try:
                pts.append((float(parts[0]), float(parts[1])))
            except ValueError:
                continue
    return pts


def merge_building_polygons(wgs_rings: List[List[Tuple[float, float]]]) -> List[List[Tuple[float, float]]]:
    """
    把多栋建筑轮廓合并为小区边界（shapely unary_union）。
    返回外环列表（多部件时返回多个环）。合并失败时退化为最长的一个环。
    """
    if not wgs_rings:
        return []
    try:
        from shapely.geometry import Polygon as _ShapelyPolygon
        from shapely.ops import unary_union
    except ImportError:
        return max(wgs_rings, key=len)

    geoms = []
    for ring in wgs_rings:
        if len(ring) < 3:
            continue
        closed = ring if ring[0] == ring[-1] else ring + [ring[0]]
        try:
            poly = _ShapelyPolygon(closed)
            if not poly.is_valid:
                poly = poly.buffer(0)
            if not poly.is_empty:
                geoms.append(poly)
        except Exception:
            continue

    if not geoms:
        return []

    merged = unary_union(geoms)
    rings: List[List[Tuple[float, float]]] = []

    def _collect(poly) -> None:
        rings.append([(round(x, 6), round(y, 6)) for x, y in poly.exterior.coords])

    if merged.geom_type == "Polygon":
        _collect(merged)
    elif merged.geom_type == "MultiPolygon":
        # 多部件时按面积从大到小，调用方通常只取第一个
        for part in sorted(merged.geoms, key=lambda g: g.area, reverse=True):
            _collect(part)

    return rings
